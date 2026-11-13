using System;
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
    /// The settlements: the places where people live and work in numbers, and where crew can be
    /// recruited (Resources/Data/crew.json). Ten of them, of four kinds:
    ///
    /// - colony stations in space: Concord Colony (a habitat ring round a hub, in orbit of
    ///   Thalassa) and Arges Station (drums and habitat blocks on a spine, round Arges);
    /// - civilian population centres: Port Ochre (a city on Ochre), Pyrois Domes (a domed town on
    ///   Jovara's moon Pyrois), Hollow Rock (a city on an Inner Belt asteroid) and the Lantern
    ///   Arcology (a habitat 2.5 km across, alone in deep space);
    /// - military bases: the Aegis Fleet Base (at Thalassa's L4) and Fort Brontes (walls, towers,
    ///   a gate, hangars, radar and a spaceport, on Jovara's moon Brontes);
    /// - industrial bases: Steropes Heavy Industries (a power plant, refinery, factory and crane
    ///   yards on Jovara's moon Steropes) and Ochre Orbital Works (in orbit of Ochre); both are
    ///   industrial sites too (industry.json).
    ///
    /// Built from the licensed kit packs (Tools/stage_carrier_assets.py stages them into the
    /// ignored Assets/Carrier/Kit: a clone without them gets the places without those pieces)
    /// and the project's own models (Tools/Blender/build_settlements.py, build_industry.py). The
    /// surface ones are a layout (Resources/Settlements, loaded only while its surface is built) put down when the frigate lands there
    /// (PlanetSurface.BuildSettlement), and the same layout, smaller, seen from orbit.
    /// </summary>
    public static partial class CarrierBuilder
    {
        // (in Resources: a layout is loaded only to build its surface, and let go with it)
        const string SettlementDir = Root + "/Resources/Settlements";
        static readonly string[] SettlementModels = { "Hab_Ring", "Mil_RadarDome", "Mil_WallSegment", "Mil_WallTower", "Mil_Gate" };
        static readonly List<string> missingPieces = new List<string>();
        static Dictionary<string, GameObject> layouts;

        static IEnumerable<string> SettlementModelPaths()
        {
            foreach (var n in SettlementModels)
            {
                string path = $"{Root}/Models/{n}.fbx";
                if (File.Exists(path)) yield return path;
            }
        }

        static GameObject KitPieceModel(string key)
        {
            var m = kit?.models?.FirstOrDefault(k => k.key == key);
            return m != null ? Load<GameObject>(m.path) : null;
        }

        /// <summary>A piece of a settlement: a kit mesh (by its staged key) or one of the project's
        /// own models ("own:Name"), under `parent`, turned, scaled, with mesh colliders on the
        /// obstacle layer, and (`mat`) all its materials one of the project's (for kit packs
        /// exported without theirs). `ground`: stood with its base at `pos` (sunk `sink`);
        /// otherwise centred on it. Null (and noted) if the piece isn't there.</summary>
        static GameObject Piece(string key, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, bool ground, string mat = null, float sink = 2f)
        {
            var src = key.StartsWith("own:") ? OwnModel(key.Substring(4)) : KitPieceModel(key);
            if (src == null) { if (!missingPieces.Contains(key)) missingPieces.Add(key); return null; }
            var go = Place(src, parent, Path.GetFileName(key));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.transform.localPosition = Vector3.zero;
            int layer = ObstacleLayer();
            var m = mat != null && Mats.TryGetValue(mat, out var found) ? found : null;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                mf.gameObject.layer = layer;
                if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                var r = mf.GetComponent<MeshRenderer>();
                if (r != null && m != null) r.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
            }
            var b = ParentBounds(go.transform);
            go.transform.localPosition += ground ? new Vector3(pos.x - b.center.x, pos.y - sink - b.min.y, pos.z - b.center.z) : pos - b.center;
            return go;
        }

        static GameObject Piece(string key, Transform parent, Vector3 pos, float yaw, float scale, bool ground = true, string mat = null, float sink = 2f) =>
            Piece(key, parent, pos, new Vector3(0f, yaw, 0f), Vector3.one * scale, ground, mat, sink);

        // the kit pieces, by their staged keys
        const string MegaDrumRinged = "Mega_City/Meshes/City_buildings/SM_large_building_module_01";
        const string MegaHall = "Mega_City/Meshes/City_buildings/SM_large_building_module_02";
        const string MegaBlock = "Mega_City/Meshes/City_buildings/SM_building_01";
        const string MegaDrum = "Mega_City/Meshes/City_buildings/SM_building_02";
        const string MegaSlabTower = "Mega_City/Meshes/City_buildings/SM_building_02_b";
        const string MegaNeedle = "Mega_City/Meshes/City_buildings/SM_building_08";
        const string MegaMain = "Mega_City/Meshes/City_buildings/SM_main_building";
        const string MegaCapsule = "Mega_City/Meshes/Background_building/SM_building_03";
        const string MegaTwinDome = "Mega_City/Meshes/Background_building/SM_building_04";
        const string MegaArcology = "Mega_City/Meshes/Background_building/SM_building_06";
        const string MegaSpire = "Mega_City/Meshes/Background_building/SM_building_08";
        const string MegaSpaceport = "Mega_City/Meshes/Landing_pad/SM_landing_pad";
        const string MegaAntenna = "Mega_City/Meshes/Antennas/SM_antenna_03";
        const string ColonyModule = "SpaceColony2086/Meshes/SM_MainModule01";
        const string ColonyBlock = "SpaceColony2086/Meshes/SM_Module03_Base09";
        const string ColonyHub = "SpaceColony2086/Meshes/SM_MainModule02_Prefab02";
        const string ColonyAntenna = "SpaceColony2086/Meshes/SM_AntennaLarge02";
        const string GateRing = "Starship/Meshes/SM_SS_Gate";
        const string VistaTall = "Volstok_District/Meshes/LP/SM_Vista_Building_02a";
        const string VistaOctagon = "Volstok_District/Meshes/LP/SM_Vista_Building_04a";
        const string VistaOffice = "Volstok_District/Meshes/LP/SM_Vista_Building_05b";
        const string TankFarm = "Volstok_District/Meshes/LP/SM_Distant_Building_01a";
        const string MonoSlab = "Monolith_megastructure/Meshes/Modules/SM_tall_module_04";
        const string MonoGenerator = "Monolith_megastructure/Meshes/Generator/SM_generator";
        const string MonoSilo = "Monolith_megastructure/Meshes/Generator/SM_generator_support";
        const string Dome = "OrbitalBase/Meshes/Domes/SM_Dome01";
        const string DomeGlass = "OrbitalBase/Meshes/Domes/SM_Dome01OuterGlass";
        const string SandDrum = "Tropical_Scifi_City/Meshes/Buildings/SM_building_02";
        const string SandHall = "Tropical_Scifi_City/Meshes/Buildings/SM_big_building";
        const string SandTower = "Tropical_Scifi_City/Meshes/Buildings/SM_tower_01";
        const string Reactor = "Sci-Fi_Flying_Cargo_Ship/Meshes/Reactor/SM_reactor";
        const string HangarShed = "Sci-Fi_Flying_Cargo_Ship/Meshes/Hangar/SM_hangar";
        const string Bunker = "Sci-Fi_Flying_Cargo_Ship/Meshes/Cargo_modules/SM_cargo_body_01";
        const string Blockhouse = "Sci-Fi_Flying_Cargo_Ship/Meshes/Cargo_modules/SM_cargo_front";
        const string ControlTower = "SciFi_Cliff/Meshes/Large_module/SM_large_module_01";
        const string PowerStation = "SilentZonePack/Assets/SM/SM_Station";
        const string Crane = "RMG_Crane/Meshes/SM_RMG_Crane_Merged";
        const string PipeRack = "Industrial_Pipe_Rack/Meshes/SM_Pipe_Rack_Straight_20m";
        const string RadarDish = "Desert_Outpost/Meshs/SM_scope";
        const string TankPlatform = "Desert_Outpost/Meshs/SM_bck_con_1";
        const string Turret = "Spaceship_Kit/SM_turret";

        // ------------------------------------------------------------------ the ground layouts

        /// <summary>The layouts of the settlements on the ground (made once a build): the landing
        /// pad ("Outpost") at the middle, the buildings round it; each child is put on the ground
        /// where it falls when the surface is built. Everything within about 850 m of the pad: a
        /// named base can be as near as 900 m to its area's edge, and what falls off the area
        /// isn't built.</summary>
        static Dictionary<string, GameObject> SettlementLayouts()
        {
            if (layouts != null) return layouts;
            layouts = new Dictionary<string, GameObject>();
            var ip = IndustryPrefabs();
            Directory.CreateDirectory(SettlementDir);
            GameObject Layout(string name, Action<Transform> build)
            {
                var root = new GameObject(name);
                if (ip.pad != null)
                {
                    var pad = (GameObject)PrefabUtility.InstantiatePrefab(ip.pad);
                    pad.transform.SetParent(root.transform, false);
                    pad.name = "Outpost";
                }
                build(root.transform);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{SettlementDir}/{name}.prefab");
                Object.DestroyImmediate(root);
                return layouts[name] = saved;
            }
            float Yaw(Vector3 at) => Mathf.Atan2(-at.x, -at.z) * Mathf.Rad2Deg;   // (facing the pad)

            Layout("Port Ochre", t =>
            {
                // the towers of the city, sand-coloured halls by the pad, offices and flats round them
                Piece(MegaCapsule, t, new Vector3(700f, 0f, 320f), 30f, 0.45f);
                Piece(MegaTwinDome, t, new Vector3(-660f, 0f, 520f), -40f, 0.4f);
                Piece(MegaSpire, t, new Vector3(220f, 0f, -820f), 10f, 0.45f);
                Piece(MegaMain, t, new Vector3(-520f, 0f, -650f), 45f, 0.6f);
                Piece(MegaSlabTower, t, new Vector3(760f, 0f, -300f), -20f, 0.5f);
                Piece(MegaNeedle, t, new Vector3(-800f, 0f, -60f), 0f, 0.7f);
                for (int i = 0; i < 5; i++)
                {
                    float a = (20f + i * 72f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 430f;
                    Piece(i % 2 == 0 ? MegaBlock : SandHall, t, at, Yaw(at), i % 2 == 0 ? 1.5f : 3f);
                }
                for (int i = 0; i < 6; i++)
                {
                    float a = (50f + i * 60f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 290f;
                    Piece(i % 2 == 0 ? SandDrum : SandTower, t, at, Yaw(at), 3f);
                }
                string[] flats = { VistaTall, VistaOctagon, VistaOffice, VistaTall, VistaOffice };
                for (int i = 0; i < flats.Length; i++)
                {
                    float a = (5f + i * 71f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 820f;
                    Piece(flats[i], t, at, Yaw(at), 0.9f);
                }
                Piece(MegaAntenna, t, new Vector3(190f, 0f, 190f), 0f, 3f);
            });

            Layout("Pyrois Domes", t =>
            {
                // four domes round the pad, a tower under each, habitat blocks between them
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 540f;
                    var d = Piece(Dome, t, at, Yaw(at), 7f, sink: 1f);
                    Piece(DomeGlass, t, at, Yaw(at), 7f, sink: 1f);
                    Piece(SandTower, t, at, Yaw(at), 2.6f);
                }
                for (int i = 0; i < 4; i++)
                {
                    float a = (i * 90f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 330f;
                    Piece(ColonyBlock, t, at, Yaw(at), 4f, mat: "Ind_Panel");
                }
                Piece(MegaDrum, t, new Vector3(0f, 0f, 780f), 0f, 0.8f);
                Piece(ColonyAntenna, t, new Vector3(260f, 0f, -260f), 0f, 5f, mat: "Ind_Panel");
                Piece(ColonyAntenna, t, new Vector3(-260f, 0f, 260f), 90f, 5f, mat: "Ind_Panel");
            });

            Layout("Fort Brontes", t =>
            {
                // a square of walls 960 m across, towers at the corners with guns on them, the gate
                // in the south wall; inside, the spaceport, hangars, bunkers, radar and the control tower
                const float half = 480f, seg = 120f;
                for (int side = 0; side < 4; side++)
                {
                    float yaw = side * 90f;
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    for (int k = 0; k < 8; k++)
                    {
                        float x = -half + seg * (k + 0.5f);
                        if (side == 0 && (k == 3 || k == 4)) continue;   // (the gate)
                        var at = rot * new Vector3(x, 0f, -half);
                        Piece("own:Mil_WallSegment", t, at, yaw, 1f);
                    }
                    var corner = rot * new Vector3(-half, 0f, -half);
                    Piece("own:Mil_WallTower", t, corner, yaw, 1f);
                    // (a gun on the tower's socket)
                    float socket = IndPointsFor("Mil_WallTower") is IndModelPoints tp && tp.deckHeight > 0f ? tp.deckHeight : 55f;
                    Piece(Turret, t, corner + Vector3.up * socket, yaw, 6f, ground: true, sink: 0f);
                }
                // the gate (150 m) in the 240 m left for it, a 45 m stub of wall either side
                Piece("own:Mil_Gate", t, new Vector3(0f, 0f, -half), 0f, 1f);
                foreach (float x in new[] { -97.5f, 97.5f })
                    Piece("own:Mil_WallSegment", t, new Vector3(x, 0f, -half), Vector3.zero, new Vector3(45f / seg, 1f, 1f), true);
                Piece(MegaSpaceport, t, new Vector3(0f, 0f, 250f), 0f, 0.75f);
                Piece(MegaHall, t, new Vector3(-310f, 0f, 120f), 90f, 0.55f);
                Piece(MegaHall, t, new Vector3(310f, 0f, 120f), -90f, 0.55f);
                Piece(Bunker, t, new Vector3(-260f, 0f, -250f), 0f, 1f);
                Piece(Blockhouse, t, new Vector3(260f, 0f, -250f), 180f, 1f);
                Piece("own:Mil_RadarDome", t, new Vector3(-380f, 0f, -380f), 0f, 1f);
                Piece("own:Mil_RadarDome", t, new Vector3(380f, 0f, 380f), 0f, 1f);
                Piece(RadarDish, t, new Vector3(380f, 0f, -380f), 45f, 1.5f);
                Piece(ControlTower, t, new Vector3(0f, 0f, -330f), 0f, 3f);
            });

            Layout("Steropes Heavy Industries", t =>
            {
                // the power plant beyond the pad, the refinery and factory either side, crane yards,
                // pipe racks between them, tank farms
                Piece(PowerStation, t, new Vector3(0f, 0f, 560f), 0f, 1f, mat: "Ind_Tank");
                if (ip.refinerySurface != null) PlaceOwn(ip.refinerySurface, t, new Vector3(-500f, 0f, -100f), 90f);
                if (ip.factorySurface != null) PlaceOwn(ip.factorySurface, t, new Vector3(520f, 0f, -100f), -90f);
                Piece(Crane, t, new Vector3(500f, 0f, -420f), 0f, 3f);
                Piece(Crane, t, new Vector3(320f, 0f, -420f), 0f, 3f);
                for (int k = 0; k < 6; k++) Piece(PipeRack, t, new Vector3(-320f, 0f, 120f + k * 50f), 0f, 2.5f, mat: "Ind_Pipe");
                Piece(TankFarm, t, new Vector3(-560f, 0f, 340f), 30f, 3f);
                Piece(TankFarm, t, new Vector3(-700f, 0f, 130f), -20f, 3f);
                Piece(TankPlatform, t, new Vector3(660f, 0f, 300f), 0f, 2.5f);
                Piece(TankPlatform, t, new Vector3(620f, 0f, 460f), 90f, 2.5f);
                Piece(MonoSilo, t, new Vector3(-220f, 0f, 640f), 0f, 2f);
                Piece(MonoSilo, t, new Vector3(220f, 0f, 640f), 0f, 2f);
            });
            if (missingPieces.Count > 0) Log($"settlements: {missingPieces.Count} pieces not there (no kit?): {string.Join(", ", missingPieces)}");
            return layouts;
        }

        /// <summary>One of the project's own prefabs placed in a layout, its base on the ground.</summary>
        static GameObject PlaceOwn(GameObject prefab, Transform parent, Vector3 pos, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        /// <summary>The surface kit's settlements (for PlanetSurface.BuildSettlement).</summary>
        static void SettlementKit(SurfaceKit kit)
        {
            kit.settlements = SettlementLayouts().Keys.ToArray();
        }

        // ------------------------------------------------------------------ in the star system

        static void Settlements(SystemRefs s, Transform parent, Built b)
        {
            var ip = IndustryPrefabs();
            WarpPoint W(string n) => s.sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == n);
            ConcordColony(s, parent, b);
            ArgesStation(s, parent, b, W("Arges"));
            HollowRock(s, parent, b, ip);
            LanternArcology(s, parent, b);
            AegisFleetBase(s, parent, b);
            OchreOrbitalWorks(s, parent, b, ip);
            var lay = SettlementLayouts();
            SurfaceSettlement(s, "Port Ochre", s.ochre.GetComponent<WarpPoint>(), -25f, 70f, lay, b, "City",
                "A city of half a million on the desert world Ochre: towers up to half a kilometre tall round its spaceport.");
            SurfaceSettlement(s, "Pyrois Domes", W("Pyrois"), 10f, -30f, lay, b, "Domed town",
                "A town under glass domes on Jovara's moon Pyrois.");
            SurfaceSettlement(s, "Fort Brontes", W("Brontes"), -20f, 60f, lay, b, "Military base",
                "The fleet's ground base on Jovara's moon Brontes: walls and gun towers round a spaceport, hangars, bunkers and radar.");
            SurfaceSettlement(s, "Steropes Heavy Industries", W("Steropes"), 25f, 150f, lay, b, "Industrial base",
                IndDescription("Steropes Heavy Industries"));
            if (missingPieces.Count > 0) Log($"settlements: {missingPieces.Count} pieces not there (no kit?)");
        }

        /// <summary>A settlement in space: its warp point, its berth, and (where it recruits) the
        /// crew; docked at with K.</summary>
        static GameObject SpaceSettlement(Transform parent, string name, Vector3 pos, Quaternion rot, WarpPoint.Kind kind, string label, string desc,
                                          float radius, Transform berth, Built b, WarpPoint orbitOf = null)
        {
            var go = parent.Find(name)?.gameObject;
            Facility(go, name, kind, false, berth, false);
            Obstruct(go, radius, radius * 0.35f, false);
            string how = IndustryTable.Get().Site(name) != null ? " Dock here (K), then U for its refinery, factory and crew." : " Dock here (K), then U to recruit crew.";
            Destination(go, name, kind, desc + how, radius, Mathf.Max(3200f, radius * 3.5f), FriendlyStation, orbitOf, false, label);
            Vector3 view = berth != null ? berth.position + (berth.position - pos).normalized * radius * 2f + Vector3.up * radius * 0.8f : pos + new Vector3(radius * 2f, radius, radius * 2f);
            b.shots.Add(($"set_{name.ToLower().Replace(' ', '_')}", view, pos));
            StreamOut(go.transform, name, radius, null, "Berth");
            return go;
        }

        static Transform NewRoot(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            return go.transform;
        }

        static void ConcordColony(SystemRefs s, Transform parent, Built b)
        {
            const string name = "Concord Colony";
            Vector3 th = s.thalassa.transform.position;
            Vector3 toStar = -th; toStar.y = 0f; toStar.Normalize();
            Vector3 dir = Quaternion.Euler(0f, 200f, 0f) * toStar;
            Vector3 pos = Above(th, RadiusOf(s.thalassa), dir * 36000f + Vector3.up * 1500f, 400000f);
            var t = NewRoot(parent, name, pos, Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir), Vector3.up));
            // the hub (a ringed drum, its axis along the ring's), the habitat ring round it, docking
            // modules and a collar at the hub's front
            var hub = Piece(MegaDrumRinged, t, Vector3.zero, new Vector3(90f, 0f, 0f), Vector3.one, false);
            float front = hub != null ? ParentBounds(hub.transform).max.z : 110f;
            Piece("own:Hab_Ring", t, Vector3.zero, Vector3.zero, Vector3.one, false);
            Piece(ColonyModule, t, new Vector3(0f, 0f, front + 60f), new Vector3(0f, 90f, 0f), Vector3.one * 5f, false, "Ind_Panel");
            Piece(GateRing, t, new Vector3(0f, 0f, front + 140f), new Vector3(90f, 0f, 0f), Vector3.one * 5f, false);
            Piece(ColonyModule, t, new Vector3(0f, 0f, -front - 60f), new Vector3(0f, 90f, 0f), Vector3.one * 5f, false, "Ind_Panel");
            IndLights(t, IndPointsFor("Hab_Ring"));
            Flood(t, new Vector3(0f, 160f, front + 100f), new Color(1f, 0.9f, 0.75f), 600f, 6f);
            var berth = Berth(t, new Vector3(170f, 0f, front + 160f), Vector3.forward);
            SpaceSettlement(parent, name, pos, t.rotation, WarpPoint.Kind.Station, "Colony station",
                "A colony station in orbit of Thalassa: a habitat ring 1.2 km across turning round its hub, home to tens of thousands.",
                650f, berth, b, s.wThalassa);
        }

        static void ArgesStation(SystemRefs s, Transform parent, Built b, WarpPoint arges)
        {
            const string name = "Arges Station";
            if (arges == null) return;
            Vector3 at = arges.Position;
            Vector3 jv = s.jovara.transform.position;
            Vector3 away = at - jv; away.y = 0f; away.Normalize();
            Vector3 pos = Above(at, arges.bodyRadius, Quaternion.Euler(0f, 90f, 0f) * away * 7000f + Vector3.up * 400f, 60000f);
            var t = NewRoot(parent, name, pos, Quaternion.LookRotation(away, Vector3.up));
            // three drums on a spine, habitat blocks round the middle one, a reactor astern, a
            // docking collar at the bow
            for (int k = -1; k <= 1; k++) Piece(MegaDrum, t, new Vector3(0f, 0f, k * 185f), new Vector3(90f, 0f, 0f), Vector3.one, false);
            for (int k = 0; k < 4; k++)
            {
                var r = Quaternion.Euler(0f, 0f, k * 90f);
                Piece(ColonyBlock, t, r * new Vector3(0f, 125f, 0f), new Vector3(0f, 0f, k * 90f), Vector3.one * 4f, false, "Ind_Panel");
            }
            Piece(Reactor, t, new Vector3(0f, 0f, -340f), new Vector3(-90f, 0f, 0f), Vector3.one * 2.5f, false);
            Piece(GateRing, t, new Vector3(0f, 0f, 320f), new Vector3(90f, 0f, 0f), Vector3.one * 5f, false);
            Piece(ColonyAntenna, t, new Vector3(0f, 170f, 120f), Vector3.zero, Vector3.one * 6f, false, "Ind_Panel");
            NavLight(t, new Vector3(90f, 0f, 300f), IndLightMat("green"), 6f, 1.6f, 0f);
            NavLight(t, new Vector3(-90f, 0f, 300f), IndLightMat("red"), 6f, 1.6f, 0.5f);
            Flood(t, new Vector3(0f, 140f, 260f), new Color(1f, 0.9f, 0.75f), 500f, 6f);
            var berth = Berth(t, new Vector3(165f, 0f, 380f), Vector3.forward);
            SpaceSettlement(parent, name, pos, t.rotation, WarpPoint.Kind.Station, "Colony station",
                "A colony station in orbit of Jovara's moon Arges: drum habitats on a spine, with habitat blocks round them.",
                420f, berth, b, arges);
        }

        static void HollowRock(SystemRefs s, Transform parent, Built b, IndPrefabs ip)
        {
            const string name = "Hollow Rock";
            Vector3 radial = new Vector3(s.innerC.x, 0f, s.innerC.z).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, radial).normalized;
            Vector3 pos = s.innerC - side * 7500f + Vector3.up * 500f;
            var go = NewRoot(parent, name, pos, Quaternion.identity).gameObject;
            var rock = BaseRock(s, go.transform, name, pos, 900f, 6601);
            Physics.SyncTransforms();
            var body = rock.transform;
            // the pad on top, the city round it: towers, slabs and two domes
            var padSpot = OnSurface(go.transform, body, Vector3.up, 880f, "Pad site", Vector3.forward, 2f);
            if (ip.pad != null) ((GameObject)PrefabUtility.InstantiatePrefab(ip.pad)).transform.SetParent(padSpot, false);
            (string key, Vector3 dir, float scale, string mat)[] city =
            {
                (VistaTall, new Vector3(0.45f, 1f, 0.1f), 0.8f, null), (VistaOctagon, new Vector3(-0.4f, 1f, 0.25f), 0.8f, null),
                (MonoSlab, new Vector3(0.1f, 1f, 0.5f), 1f, null), (MonoSlab, new Vector3(0.2f, 1f, -0.5f), 1f, null),
                (Dome, new Vector3(-0.5f, 1f, -0.3f), 5f, null), (DomeGlass, new Vector3(-0.5f, 1f, -0.3f), 5f, null),
                (Dome, new Vector3(0.55f, 1f, -0.25f), 4f, null), (DomeGlass, new Vector3(0.55f, 1f, -0.25f), 4f, null),
                (VistaOffice, new Vector3(-0.2f, 1f, 0.55f), 1f, null), (SandTower, new Vector3(0.6f, 1f, 0.35f), 4f, null),
            };
            int i = 0;
            foreach (var (key, dir, scale, mat) in city)
            {
                var spot = OnSurface(go.transform, body, dir.normalized, 880f, $"City {i++}", Vector3.forward, 4f);
                Piece(key, spot, Vector3.zero, 0f, scale, true, mat, 0f);
            }
            float deck = ip.padPoints != null && ip.padPoints.deckHeight > 0f ? ip.padPoints.deckHeight : 6f;
            var berth = Berth(padSpot, new Vector3(0f, deck + FrigateKeel(), 0f), Vector3.forward);
            Facility(go, name, WarpPoint.Kind.AsteroidBase, false, berth, false);
            Destination(go, name, WarpPoint.Kind.AsteroidBase,
                "A city on a big rock of the Inner Belt: towers and domes round a landing pad. Dock (K) to set down on its pad, then U to recruit crew.",
                900f, 3600f, FriendlyStation, null, false, "Asteroid city");
            b.shots.Add(("set_hollow_rock", padSpot.position + Vector3.up * 1200f - side * 1500f, padSpot.position));
            StreamOut(go.transform, name, 900f, body, rock.name, "Pad site");
        }

        static void LanternArcology(SystemRefs s, Transform parent, Built b)
        {
            const string name = "Lantern Arcology";
            Vector3 pos = Polar(30f, 115f, -3f);
            Vector3 toStar = -pos; toStar.y = 0f; toStar.Normalize();
            var t = NewRoot(parent, name, pos, Quaternion.LookRotation(toStar, Vector3.up));
            // the arcology: a slab of towers 2.6 km wide, its docks at its foot
            var slab = Piece(MegaArcology, t, Vector3.zero, Vector3.zero, Vector3.one * 0.6f, false);
            var bb = slab != null ? ParentBounds(slab.transform) : new Bounds(Vector3.zero, new Vector3(2600f, 2400f, 900f));
            float frontZ = bb.max.z, foot = bb.min.y;
            Piece(GateRing, t, new Vector3(-220f, foot + 260f, frontZ + 80f), new Vector3(90f, 0f, 0f), Vector3.one * 8f, false);
            Piece(GateRing, t, new Vector3(220f, foot + 260f, frontZ + 80f), new Vector3(90f, 0f, 0f), Vector3.one * 8f, false);
            Piece(ColonyHub, t, new Vector3(0f, foot + 200f, frontZ + 140f), new Vector3(0f, 90f, 0f), Vector3.one * 6f, false, "Ind_Panel");
            for (int k = 0; k < 6; k++)
                Flood(t, new Vector3(-1000f + k * 400f, foot + 300f + (k % 2) * 600f, frontZ + 120f), new Color(1f, 0.88f, 0.7f), 900f, 7f);
            NavLight(t, new Vector3(bb.min.x, bb.max.y, 0f), IndLightMat("red"), 14f, 1.8f, 0f);
            NavLight(t, new Vector3(bb.max.x, bb.max.y, 0f), IndLightMat("green"), 14f, 1.8f, 0.5f);
            var berth = Berth(t, new Vector3(0f, foot + 260f, frontZ + 420f), Vector3.forward);
            SpaceSettlement(parent, name, pos, t.rotation, WarpPoint.Kind.Station, "Habitat",
                "An arcology alone in deep space between Cinder and the Jovara Trojans: a slab of towers 2.6 km across, home to a quarter of a million.",
                1500f, berth, b);
        }

        static void AegisFleetBase(SystemRefs s, Transform parent, Built b)
        {
            const string name = "Aegis Fleet Base";
            Vector3 pos = LagrangeAtStart(s.thalassa, 4);
            Vector3 toTh = s.thalassa.transform.position - pos; toTh.y = 0f; toTh.Normalize();
            var t = NewRoot(parent, name, pos, Quaternion.LookRotation(toTh, Vector3.up));
            // two hangar halls back to back, armoured blocks round them, gun turrets, a reactor
            // pair, a command tower and a radar dome
            Piece(MegaHall, t, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f), Vector3.one, false);
            Piece(MegaHall, t, new Vector3(0f, -185f, 0f), new Vector3(180f, 0f, 0f), Vector3.one, false);
            Piece(Bunker, t, new Vector3(-170f, -90f, 150f), Vector3.zero, Vector3.one * 1.5f, false);
            Piece(Bunker, t, new Vector3(170f, -90f, 150f), Vector3.zero, Vector3.one * 1.5f, false);
            Piece(Blockhouse, t, new Vector3(0f, -90f, -230f), new Vector3(0f, 180f, 0f), Vector3.one * 1.6f, false);
            Piece(HangarShed, t, new Vector3(-150f, 110f, -80f), Vector3.zero, Vector3.one * 3f, false);
            Piece(HangarShed, t, new Vector3(150f, 110f, -80f), Vector3.zero, Vector3.one * 3f, false);
            Piece(MonoGenerator, t, new Vector3(-90f, -90f, -300f), new Vector3(90f, 0f, 0f), Vector3.one * 2f, false);
            Piece(MonoGenerator, t, new Vector3(90f, -90f, -300f), new Vector3(90f, 0f, 0f), Vector3.one * 2f, false);
            Piece(ControlTower, t, new Vector3(0f, 150f, 120f), Vector3.zero, Vector3.one * 3f, false);
            Piece("own:Mil_RadarDome", t, new Vector3(0f, 120f, -200f), Vector3.zero, Vector3.one, false);
            foreach (var (x, y, z) in new[] { (-120f, 100f, 180f), (120f, 100f, 180f), (-120f, -280f, 180f), (120f, -280f, 180f), (0f, 100f, -300f), (0f, -280f, -300f) })
                Piece(Turret, t, new Vector3(x, y, z), new Vector3(y < 0f ? 180f : 0f, 0f, 0f), Vector3.one * 9f, false);
            NavLight(t, new Vector3(130f, 0f, 200f), IndLightMat("green"), 6f, 1.4f, 0f);
            NavLight(t, new Vector3(-130f, 0f, 200f), IndLightMat("red"), 6f, 1.4f, 0.5f);
            Flood(t, new Vector3(0f, 0f, 260f), new Color(0.85f, 0.9f, 1f), 600f, 6f);
            var berth = Berth(t, new Vector3(290f, -90f, 120f), Vector3.forward);
            SpaceSettlement(parent, name, pos, t.rotation, WarpPoint.Kind.Lagrange, "Military base (L4)",
                "The fleet's base at Thalassa's L4 point, 60 degrees ahead of the planet: hangars, armoured blocks and gun turrets, and the crews for the fleet's ships.",
                420f, berth, b);
        }

        static void OchreOrbitalWorks(SystemRefs s, Transform parent, Built b, IndPrefabs ip)
        {
            const string name = "Ochre Orbital Works";
            if (ip.factorySpace == null) return;
            Vector3 oc = s.ochre.transform.position;
            Vector3 toStar = -oc; toStar.y = 0f; toStar.Normalize();
            Vector3 dir = Quaternion.Euler(0f, 100f, 0f) * toStar;
            Vector3 pos = Above(oc, RadiusOf(s.ochre), dir * 16000f + Vector3.up * 600f, 250000f);
            var t = NewRoot(parent, name, pos, Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir), Vector3.up));
            // the factory yard and the refinery side by side, cranes and a reactor between them
            var factory = PlaceOwn(ip.factorySpace, t, Vector3.zero, 0f);
            if (ip.refinerySpace != null) PlaceOwn(ip.refinerySpace, t, new Vector3(-720f, 0f, 0f), 180f);
            Piece(Crane, t, new Vector3(-330f, 160f, 120f), Vector3.zero, Vector3.one * 4f, false);
            Piece(Crane, t, new Vector3(-330f, 160f, -160f), Vector3.zero, Vector3.one * 4f, false);
            Piece(Reactor, t, new Vector3(-360f, -40f, 0f), new Vector3(-90f, 0f, 0f), Vector3.one * 3f, false);
            var berth = OwnBerth(t, factory.transform.Find("Berth"));
            t.gameObject.AddComponent<IndustrySite>().siteName = name;
            SpaceSettlement(parent, name, pos, t.rotation, WarpPoint.Kind.Installation, "Industrial base",
                IndDescription(name), 800f, berth, b, s.ochre.GetComponent<WarpPoint>());
        }

        /// <summary>A settlement on a planet's or moon's surface as seen from orbit (its layout, to
        /// scale with the body, each building on the ground), and the destination over it: landing
        /// there comes down beside it, where the surface builds it in full.</summary>
        static void SurfaceSettlement(SystemRefs s, string name, WarpPoint body, float latitude, float sunAngle, Dictionary<string, GameObject> lay,
                                      Built b, string label, string desc)
        {
            if (body == null || !lay.TryGetValue(name, out var layout)) { Log($"settlements: no body or layout for {name}"); return; }
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
            go.transform.localScale = Vector3.one / Mathf.Max(1e-6f, bt.lossyScale.x);
            go.transform.SetPositionAndRotation(bt.position + dir * radius, Quaternion.FromToRotation(Vector3.up, dir));
            // to scale with the body: the layout spans no more than a quarter of its radius
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(layout);
            inst.transform.SetParent(go.transform, false);
            float span = Mathf.Max(200f, LocalBounds(inst.transform).extents.magnitude);
            float k = Mathf.Min(0.5f, radius * 0.25f / span);
            inst.transform.localScale = Vector3.one * k;
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            Physics.SyncTransforms();
            foreach (Transform c in inst.transform.Cast<Transform>().ToArray())
            {
                var an = c.gameObject.AddComponent<SurfaceAnchor>();
                an.body = bt;
                an.sink = 1f;
                an.Settle();
            }
            Flood(go.transform, new Vector3(0f, 120f, 0f), new Color(1f, 0.88f, 0.7f), 500f, 6f);
            string how = IndustryTable.Get().Site(name) != null ? "set down on the pad, then U for its refinery, factory and crew." : "set down on the pad, then U to recruit crew.";
            var wp = Destination(go, name, WarpPoint.Kind.SurfaceBase, $"{desc} From orbit over it, land (K), {how}", 300f, 2600f, FriendlyWorks, body, false, label);
            wp.landsOn = body;
            wp.overhead = true;
            wp.surface = body.surface;
            Facility(go, name, WarpPoint.Kind.SurfaceBase, false);
            Vector3 tangent = Vector3.Cross(dir, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.5f) tangent = Vector3.right;
            b.shots.Add(($"set_{name.ToLower().Replace(' ', '_')}", go.transform.position + dir * 900f + tangent * 700f, go.transform.position));
            StreamOut(go.transform, name, 800f, bt);
        }
    }
}
