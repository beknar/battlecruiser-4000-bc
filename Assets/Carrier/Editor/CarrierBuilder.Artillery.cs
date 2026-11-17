using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Maw's long-range weapons and the Vrask siege (Data/vrask.json: the Maw's "artillery"
    /// and "nukes", and "siege"): its spinal artillery (ArtilleryGun at its spinal_muzzle, firing
    /// Prefabs/VraskArtilleryShell), its nuclear silos (NukeLauncher at its nuke_cell points,
    /// Prefabs/VraskNuke) and its tactics (MawTactics); and the bombardment group on station over
    /// a city on a world with air (VraskBombardment), built like the corsairs' strike satellites:
    /// in a frame turning with the planet, a hostile destination that fights back.
    /// </summary>
    public static partial class CarrierBuilder
    {
        static ArtilleryShell vraskShell;
        static NukeMissile vraskNuke;

        /// <summary>The artillery shell: a long green-white streak, slow, bursting wide.</summary>
        static ArtilleryShell MakeArtilleryShell(Prefabs p, VraskArtillery a)
        {
            var go = new GameObject("VraskArtilleryShell");
            var vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            StripColliders(vis);
            vis.name = "Shell";
            vis.transform.SetParent(go.transform, false);
            vis.transform.localRotation = Quaternion.Euler(90, 0, 0);
            vis.transform.localScale = new Vector3(4f, 18f, 4f);
            vis.transform.localPosition = new Vector3(0, 0, -18f);
            var glow = a.color != null && a.color.Length >= 4 ? new Color(a.color[0], a.color[1], a.color[2]) * a.color[3] : new Color(0.6f, 1f, 0.45f) * 8f;
            var r = vis.GetComponent<MeshRenderer>();
            r.sharedMaterial = Additive("FX_VraskShell", glow);
            r.shadowCastingMode = ShadowCastingMode.Off;
            var trail = Node("Trail", go.transform, new Vector3(0, 0, -30f)).AddComponent<TrailRenderer>();
            trail.time = 2.5f;
            trail.startWidth = 7f;
            trail.endWidth = 0f;
            trail.sharedMaterial = r.sharedMaterial;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            var s = go.AddComponent<ArtilleryShell>();
            s.speed = a.shellSpeed > 0f ? a.shellSpeed : 550f;
            s.damage = a.damage > 0f ? a.damage : 700f;
            s.blastRadius = a.blastRadius > 0f ? a.blastRadius : 500f;
            s.edge = a.edge;
            s.maxFlight = Mathf.Max(20f, (a.range > 0f ? a.range : 13000f) / s.speed + 5f);
            s.blastEffect = p.explosion;
            s.flashColor = glow / Mathf.Max(1f, glow.maxColorComponent);
            Log($"vrask artillery shell: {s.speed:0} m/s, {s.damage:0} at the centre of a {s.blastRadius:0} m blast ({s.edge:P0} at its edge)");
            return SavePrefab<ArtilleryShell>(go, "VraskArtilleryShell");
        }

        /// <summary>The nuclear missile: the missile's model, bigger and slower, a white trail.</summary>
        static NukeMissile MakeNuke(Prefabs p, VraskNukes n)
        {
            var go = new GameObject("VraskNuke");
            var model = Place(OwnModel("Missile"), go.transform, "Model");
            model.transform.localScale *= 2.4f;
            var m = go.AddComponent<NukeMissile>();
            m.explosion = p.explosion;
            m.boostTime = 1.6f;
            m.boostSpeed = 80f;
            m.maxSpeed = n.speed > 0f ? n.speed : 320f;
            m.acceleration = 60f;
            m.turnRate = 25f;
            m.lifetime = Mathf.Max(45f, (n.range > 0f ? n.range : 9000f) / m.maxSpeed + 25f);
            m.proximity = 60f;
            m.damage = n.damage > 0f ? n.damage : 3000f;
            m.blastRadius = n.blastRadius > 0f ? n.blastRadius : 1200f;
            m.edge = n.edge;
            m.toughness = n.toughness > 0f ? n.toughness : 32f;
            m.hitRadius = 12f;
            var trail = Node("Trail", go.transform, new Vector3(0, 0, -10f)).AddComponent<TrailRenderer>();
            trail.time = 1.6f;
            trail.startWidth = 4f;
            trail.endWidth = 0f;
            trail.sharedMaterial = Additive("FX_VraskNukeTrail", new Color(1f, 0.85f, 0.6f) * 3f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            Log($"vrask nuclear missile: {m.maxSpeed:0} m/s, {m.damage:0} at the centre of a {m.blastRadius:0} m blast in space, toughness {m.toughness:0}");
            return SavePrefab<NukeMissile>(go, "VraskNuke");
        }

        /// <summary>The Maw's spinal artillery at its spinal_muzzle, its nuclear silos at its
        /// nuke_cells, and its tactics.</summary>
        static void AddArtillery(Prefabs p, GameObject root, VraskShip vs, VraskShipStats s)
        {
            var a = s.artillery;
            if (a == null || a.range <= 0f) return;
            if (vraskShell == null) vraskShell = MakeArtilleryShell(p, a);
            var muzzle = vs.PointOf(string.IsNullOrEmpty(a.on) ? "spinal_muzzle" : a.on);
            var gun = Node("Artillery", root.transform).AddComponent<ArtilleryGun>();
            gun.shellPrefab = vraskShell;
            gun.muzzle = muzzle != null ? muzzle : root.transform;
            gun.label = string.IsNullOrEmpty(a.label) ? "Spinal artillery" : a.label;
            gun.reload = a.interval > 0f ? a.interval : 16f;
            gun.range = a.range;
            gun.minRange = a.minRange > 0f ? a.minRange : 1500f;
            gun.cone = a.cone > 0f ? a.cone : 20f;
            string silos = "no silos";
            var n = s.nukes;
            if (n != null && n.stock > 0)
            {
                if (vraskNuke == null) vraskNuke = MakeNuke(p, n);
                var nl = root.AddComponent<NukeLauncher>();
                nl.nukePrefab = vraskNuke;
                nl.cells = vs.PointsOf("nuke_cell");
                if (nl.cells.Length == 0) nl.cells = new[] { root.transform };
                nl.capacity = n.stock;
                nl.range = n.range > 0f ? n.range : 9000f;
                nl.interval = n.interval > 0f ? n.interval : 90f;
                silos = $"{n.stock} nuclear missiles in {nl.cells.Length} silos, {nl.range / 1000f:0.#} km";
            }
            root.AddComponent<MawTactics>();
            Log($"{s.displayName}: {gun.label} from {(muzzle != null ? muzzle.name : "the hull (no spinal_muzzle)")}, every {gun.reload:0} s, " +
                $"{gun.minRange / 1000f:0.#} to {gun.range / 1000f:0.#} km, {gun.cone:0} deg off the bow; {silos}; Maw tactics");
        }

        // ------------------------------------------------------------------ the siege

        /// <summary>
        /// The Vrask siege group (Data/vrask.json "siege"): the Maw and its escorts on station
        /// over a city on a world with air, turning with it, raiding it from orbit on the game's
        /// clock (VraskBombardment). A hostile destination: its guns open up on a ship that comes
        /// near (DefenseGrid), its artillery on one its escorts can see. Destroyed, the Maw comes
        /// back in its own time, and the raids with it.
        /// </summary>
        static void VraskSiege(SystemRefs s, Prefabs p, Transform parent, Built b)
        {
            var g = vrask?.siege;
            if (g == null || string.IsNullOrEmpty(g.at)) { Log("vrask: no siege"); return; }
            var tw = s.sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == g.at);
            if (tw == null || tw.landsOn == null) { Log($"vrask siege: no settlement '{g.at}' on a body"); return; }
            if (!CarrierGame.Demo.VraskBombardment.HasAir(tw.landsOn)) { Log($"vrask siege: {g.at} is on {tw.landsOn.displayName}, which has no air"); return; }
            if (!p.vrask.TryGetValue(string.IsNullOrEmpty(g.ship) ? "Maw" : g.ship, out var mawPrefab) || mawPrefab == null) { Log("vrask siege: no Maw prefab"); return; }
            p.vrask.TryGetValue(string.IsNullOrEmpty(g.escort) ? "Skink" : g.escort, out var escortPrefab);
            var body = tw.landsOn.transform;
            Vector3 radial = (tw.Position - body.position).normalized;
            Vector3 along = Vector3.Cross(radial, Vector3.up);
            var f = BodyFrame(body, g.name, radial, along.sqrMagnitude > 0.01f ? along : Vector3.right);
            Object.DestroyImmediate(f.GetComponent<RaiseToGround>());   // (it holds station in space, not on the ground)
            float R = tw.landsOn.bodyRadius, alt = g.altitude > 0f ? g.altitude : 160000f;
            var station = Node(g.name, f, new Vector3(0f, R + alt, g.side), Quaternion.identity);
            // the Maw bow down at the city; its escorts either side of it, level
            GameObject Ship(GameObject prefab, string name, Vector3 at, Quaternion rot)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = name;
                go.transform.SetParent(station.transform, false);
                go.transform.localPosition = at;
                go.transform.localRotation = rot;
                var info = go.GetComponent<TargetInfo>();
                if (info != null) info.displayName = name;
                var h = go.GetComponent<Health>();
                if (h != null) h.respawnDelay = g.respawnMinutes > 0f ? g.respawnMinutes * 60f : 1800f;
                return go;
            }
            var maw = Ship(mawPrefab, "Vrask Siege Maw", Vector3.zero, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var guns = new System.Collections.Generic.List<EnemyWeapons> { maw.GetComponent<EnemyWeapons>() };
            int escorts = escortPrefab != null ? Mathf.Max(0, g.escorts) : 0;
            for (int i = 0; i < escorts; i++)
            {
                float side = (i % 2 == 0 ? 1f : -1f) * (1400f + 300f * (i / 2));
                var e = Ship(escortPrefab, $"Vrask Siege Skink {(char)('A' + i)}", new Vector3(side, 200f * (i % 2 == 0 ? 1f : -1f), 300f), Quaternion.identity);
                guns.Add(e.GetComponent<EnemyWeapons>());
            }
            var grid = station.AddComponent<DefenseGrid>();
            grid.gridName = g.name;
            grid.weapons = guns.Where(w => w != null).ToArray();
            grid.engageRange = g.engageRange > 0f ? g.engageRange : 12000f;
            grid.disengageRange = grid.engageRange + 4000f;
            // (its fleet coordination, on station: the Skinks spot and screen, and go quiet with the Maw lost)
            AddFleet(station, guns.Where(w => w != null).Select(w => w.GetComponent<VraskShip>()), null, grid);
            var siege = maw.AddComponent<CarrierGame.Demo.VraskBombardment>();
            siege.target = g.at;
            siege.battery = maw.GetComponent<Health>();
            if (g.intervalMinutes > 0f) siege.intervalMinutes = g.intervalMinutes;
            if (g.shells > 0) siege.shells = g.shells;
            if (g.shellRadius > 0f) siege.shellRadius = g.shellRadius;
            if (g.siegeReach > 0f) siege.siegeReach = g.siegeReach;
            siege.fortified = g.fortified;
            siege.nukeChance = g.nukeChance;
            if (g.nukeRestockMinutes > 0f) siege.nukeRestockMinutes = g.nukeRestockMinutes;
            if (g.nukeRebuildMinutes > 0f) siege.nukeRebuildMinutes = g.nukeRebuildMinutes;
            if (g.nukeMargin > 0f) siege.nukeMargin = g.nukeMargin;
            if (g.nukePopulationShare > 0f) siege.nukePopulationShare = g.nukePopulationShare;
            Facility(station, g.name, WarpPoint.Kind.Weapons, true);
            Destination(station, g.name, WarpPoint.Kind.Weapons, g.note ?? "", 2000f, g.arrival > 0f ? g.arrival : 16000f, HostileWorks, tw.landsOn, true);
            if (!string.IsNullOrEmpty(g.cityNote) && (tw.description == null || !tw.description.Contains(g.cityNote))) tw.description = (tw.description + " " + g.cityNote).Trim();
            b.targets.Add(maw.transform);
            Log($"vrask siege: {g.name} over {g.at} on {tw.landsOn.displayName} (air: yes), {alt / 1000f:0} km up, the Maw and {escorts} escorts; " +
                $"a raid every {siege.intervalMinutes:0} min ({siege.shells} shells of {siege.shellRadius:0} m), a nuclear strike on {(siege.fortified ? "it (fortified)" : "defences only")} " +
                $"{siege.nukeChance:P0} of raids, at most every {siege.nukeRestockMinutes:0} min, ruins for {siege.nukeRebuildMinutes:0} min " +
                $"(the city's reach {CarrierGame.Demo.OrbitalStrikes.SettlementReach(SettlementLayouts().TryGetValue(g.at, out var lay) ? lay : null):0} m)");
        }
    }
}
