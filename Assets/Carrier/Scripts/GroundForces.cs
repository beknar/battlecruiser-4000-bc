using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>
    /// The ground forces of a hostile world, built with each area of its surface (and gone with
    /// it): gun batteries (a heavy triple turret on a bunker), missile sites, hover tanks that
    /// patrol and chase, fighters on patrol overhead, and bases whose launch pads hold fighters
    /// (and sometimes a gunship) that take off when the alarm goes up. Anything destroyed stays
    /// destroyed when the area is built again.
    /// </summary>
    public static class GroundForces
    {
        public delegate (Vector3 ground, float yawTurn) Placer(double u, double v);

        public static GroundGarrison Deploy(PlanetSurface surface, Transform area, Transform pad, TileFeatures f, Placer place, long keyBase)
        {
            var kit = surface.Kit;
            var g = area.gameObject.AddComponent<GroundGarrison>();
            g.surface = surface;
            int padShips = 0;
            bool gunship = false, seaBase = false;
            float sea = surface.SeaLevel;
            foreach (var s in f.units) if (s.kind == UnitKind.PadGunship) gunship = true;
            foreach (var s in f.units)
            {
                long key = keyBase | (uint)s.id;
                if (surface.Destroyed.Contains(key)) continue;
                var (ground, turn) = place(s.u, s.v);
                float yaw = s.yaw + turn;
                GameObject go = null;
                switch (s.kind)
                {
                    case UnitKind.Battery:
                        if (ground.y < surface.SeaLevel + 1f) continue;
                        go = Battery(kit, area, ground, yaw);
                        break;
                    case UnitKind.MissileSite:
                        if (ground.y < surface.SeaLevel + 1f) continue;
                        go = MissileSite(kit, area, ground, yaw);
                        break;
                    case UnitKind.Tank:
                        go = Tank(kit, area, surface, ground, yaw);
                        break;
                    case UnitKind.SeaBase:
                        // one to an area, on a sea floor deep enough to hide it
                        if (seaBase || sea - ground.y < Stats.seaBase.minDepth) continue;
                        go = SeaBase(kit, area, ground, yaw);
                        seaBase = go != null;
                        break;
                    case UnitKind.Submarine:
                        if (sea - ground.y < Stats.submarine.minDepth) continue;
                        go = Submarine(kit, area, surface, ground, sea, yaw);
                        break;
                    case UnitKind.Patrol:
                        go = Flyer(kit.fighter, area, ground + Vector3.up * Stats.fighter.patrolHeight, yaw, "Patrol Fighter", false, 0f);
                        break;
                    case UnitKind.PadFighter:
                    case UnitKind.PadGunship:
                    {
                        if (pad == null) continue;
                        bool big = s.kind == UnitKind.PadGunship;
                        var prefab = big ? kit.gunship : (padShips % 2 == 0 || kit.drone == null ? kit.fighter : kit.drone);
                        if (prefab == null) continue;
                        Vector3 local = big ? new Vector3(0f, 0f, 10f) : new Vector3(padShips % 2 == 0 ? -85f : 85f, 0f, gunship ? -70f : 0f);
                        if (!big) padShips++;
                        Vector3 at = pad.TransformPoint(local);
                        float half = HalfHeight(prefab);
                        go = Flyer(prefab, area, at + Vector3.up * (half + 0.5f), pad.eulerAngles.y + (big ? 0f : 180f * (padShips % 2)),
                                   big ? "Gunship" : "Base Fighter", true, big ? 1f : 0f);
                        break;
                    }
                }
                if (go == null) continue;
                if (!go.TryGetComponent(out GroundUnit unit)) unit = go.AddComponent<GroundUnit>();
                unit.key = key;
                unit.rebuildMinutes = RebuildMinutes(s.kind);
                unit.garrison = g;
                unit.surface = surface;
                g.units.Add(unit);
            }
            if (g.units.Count == 0) { Object.Destroy(g); return null; }
            g.center = pad != null ? pad.position : g.units[0].transform.position;
            return g;
        }

        static float HalfHeight(GameObject prefab)
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            return any ? -b.min.y : 5f;
        }

        // ------------------------------------------------------------------ the pieces

        static GameObject Part(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        static void Light(Transform parent, Vector3 pos, Material mat, float range)
        {
            var b = Part(PrimitiveType.Sphere, parent, "Warning light", pos, Vector3.one * 1.6f, mat);
            b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var l = new GameObject("Light").AddComponent<UnityEngine.Light>();
            l.transform.SetParent(b.transform, false);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.25f, 0.15f);
            l.range = range;
            l.intensity = 3f;
            l.shadows = LightShadows.None;
            b.AddComponent<Blink>();
        }

        static void Identity(GameObject go, string display, string cls, string weapons, string threat, TargetInfo.Kind kind)
        {
            if (!go.TryGetComponent(out TargetInfo ti)) ti = go.AddComponent<TargetInfo>();
            ti.displayName = display;
            ti.shipClass = cls;
            ti.faction = "Hostile";
            ti.weapons = weapons;
            ti.threat = threat;
            ti.kind = kind;
        }

        /// <summary>The units' numbers (Resources/Data/ground_units.json).</summary>
        static GroundUnitTable Stats => GroundUnitTable.Get();

        static void Identity(GameObject go, GroundUnitStats s, TargetInfo.Kind kind) => Identity(go, s.name, s.shipClass, s.weapons, s.threat, kind);

        static Health Hull(GameObject go, SurfaceKit kit, GroundUnitStats s) => Hull(go, kit, s.hull, s.explosion);

        static Health Hull(GameObject go, SurfaceKit kit, float hp, float boom)
        {
            if (!go.TryGetComponent(out Health h)) h = go.AddComponent<Health>();   // its events come from Health.Awake
            h.maxHealth = hp;
            h.respawnDelay = 0f;
            h.deathEffect = boom > 0.8f || kit.explosionSmall == null ? kit.explosion : kit.explosionSmall;
            h.deathEffectScale = boom;
            return h;
        }

        /// <summary>How long a kind of unit takes to be rebuilt once destroyed (ground_units.json; 0: never).</summary>
        public static float RebuildMinutesOf(UnitKind k) => RebuildMinutes(k);

        static float RebuildMinutes(UnitKind k)
        {
            var t = Stats;
            switch (k)
            {
                case UnitKind.Battery: return t.battery?.rebuildMinutes ?? 0f;
                case UnitKind.MissileSite: return t.missileSite?.rebuildMinutes ?? 0f;
                case UnitKind.Tank: return t.tank?.rebuildMinutes ?? 0f;
                case UnitKind.Patrol: case UnitKind.PadFighter: return t.fighter?.rebuildMinutes ?? 0f;
                case UnitKind.PadGunship: return t.gunship?.rebuildMinutes ?? 0f;
                case UnitKind.SeaBase: return t.seaBase?.rebuildMinutes ?? 0f;
                case UnitKind.Submarine: return t.submarine?.rebuildMinutes ?? 0f;
            }
            return 0f;
        }

        /// <summary>A heavy triple turret on a bunker.</summary>
        static GameObject Battery(SurfaceKit kit, Transform area, Vector3 ground, float yaw)
        {
            if (kit.battery == null) return null;
            var s = Stats.battery;
            var go = new GameObject("Ground Battery");
            go.SetActive(false);
            go.transform.SetParent(area, false);
            go.transform.SetPositionAndRotation(ground + Vector3.up * 7f, Quaternion.Euler(0f, yaw, 0f));
            Part(PrimitiveType.Cylinder, go.transform, "Bunker", new Vector3(0f, -6f, 0f), new Vector3(36f, 7f, 36f), kit.bunker);
            Part(PrimitiveType.Cylinder, go.transform, "Ring", new Vector3(0f, 0.6f, 0f), new Vector3(24f, 0.6f, 24f), kit.armor);
            Light(go.transform, new Vector3(16f, 1.5f, 0f), kit.warning, 60f);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -2f, 0f);
            col.size = new Vector3(34f, 18f, 34f);
            var t = Object.Instantiate(kit.battery, go.transform);
            t.name = "Turret";
            t.transform.localPosition = new Vector3(0f, 1f, -6f);
            t.transform.localRotation = Quaternion.identity;
            t.transform.localScale = Vector3.one * 0.55f;
            t.projectilePrefab = kit.cannon;
            t.range = s.range;
            t.fireInterval = s.fireInterval;
            t.fireCone = s.fireCone;
            t.leadTarget = true;
            t.yawLimit = s.yawLimit;
            t.minPitch = s.minPitch;
            t.maxPitch = s.maxPitch;
            t.yawSpeed = s.yawSpeed;
            t.pitchSpeed = s.pitchSpeed;
            t.autoFire = true;
            t.target = null;
            var w = go.AddComponent<EnemyWeapons>();
            w.turrets = new[] { t };
            w.holdFire = 0f;
            w.weaponsFree = false;
            Hull(go, kit, s);
            Identity(go, s, TargetInfo.Kind.Capital);
            go.SetActive(true);
            return go;
        }

        /// <summary>A bunker with a box of missile cells raised on it.</summary>
        static GameObject MissileSite(SurfaceKit kit, Transform area, Vector3 ground, float yaw)
        {
            if (kit.missile == null) return null;
            var s = Stats.missileSite;
            var go = new GameObject("Missile Site");
            go.SetActive(false);
            go.transform.SetParent(area, false);
            go.transform.SetPositionAndRotation(ground + Vector3.up * 5f, Quaternion.Euler(0f, yaw, 0f));
            Part(PrimitiveType.Cube, go.transform, "Bunker", new Vector3(0f, -4f, 0f), new Vector3(26f, 8f, 22f), kit.bunker);
            Part(PrimitiveType.Cube, go.transform, "Launcher", new Vector3(0f, 4f, 0f), new Vector3(10f, 6f, 13f), kit.armor, new Vector3(-55f, 0f, 0f));
            for (int i = 0; i < 4; i++)
                Part(PrimitiveType.Cylinder, go.transform, "Cell", new Vector3(-2.7f + (i % 2) * 5.4f, 6.5f + (i / 2) * 2.2f, 2.5f - (i / 2) * 3.4f),
                     new Vector3(3.6f, 0.3f, 3.6f), kit.warning, new Vector3(-55f, 0f, 0f));
            Light(go.transform, new Vector3(12f, 0.5f, 10f), kit.warning, 50f);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, 0f);
            col.size = new Vector3(26f, 16f, 22f);
            var w = go.AddComponent<EnemyWeapons>();
            w.missilePrefab = kit.missile;
            w.missileRange = s.missileRange;
            w.salvoInterval = s.salvoInterval;
            w.salvoSize = s.salvoSize;
            w.holdFire = 0f;
            w.weaponsFree = false;
            Hull(go, kit, s);
            Identity(go, s, TargetInfo.Kind.Capital);
            go.SetActive(true);
            return go;
        }

        /// <summary>A hover tank: a wedge hull on skirts with a turret and a long gun.</summary>
        static GameObject Tank(SurfaceKit kit, Transform area, PlanetSurface surface, Vector3 ground, float yaw)
        {
            var s = Stats.tank;
            var go = new GameObject("Hover Tank");
            go.SetActive(false);
            go.transform.SetParent(area, false);
            go.transform.SetPositionAndRotation(ground + Vector3.up * 4f, Quaternion.Euler(0f, yaw, 0f));
            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            Part(PrimitiveType.Cube, body, "Hull", new Vector3(0f, 0f, 0f), new Vector3(11f, 3f, 18f), kit.armor);
            Part(PrimitiveType.Cube, body, "Glacis", new Vector3(0f, 0.4f, 9.2f), new Vector3(10f, 2.4f, 5f), kit.armor, new Vector3(28f, 0f, 0f));
            Part(PrimitiveType.Cube, body, "Skirt L", new Vector3(-6f, -1.4f, 0f), new Vector3(1.4f, 2f, 19f), kit.bunker);
            Part(PrimitiveType.Cube, body, "Skirt R", new Vector3(6f, -1.4f, 0f), new Vector3(1.4f, 2f, 19f), kit.bunker);
            var glow = Part(PrimitiveType.Cube, body, "Hover glow", new Vector3(0f, -2.2f, 0f), new Vector3(9f, 0.3f, 16f), kit.warning);
            glow.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var turret = new GameObject("Turret").transform;
            turret.SetParent(body, false);
            turret.localPosition = new Vector3(0f, 2.2f, -1.5f);
            Part(PrimitiveType.Cylinder, turret, "Dome", Vector3.zero, new Vector3(7f, 1.2f, 7f), kit.armor);
            Part(PrimitiveType.Cylinder, turret, "Gun", new Vector3(0f, 0.4f, 6.5f), new Vector3(0.9f, 5f, 0.9f), kit.bunker, new Vector3(90f, 0f, 0f));
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(13f, 6f, 21f);
            var w = go.AddComponent<EnemyWeapons>();
            w.gunPrefab = kit.cannon != null ? kit.cannon : kit.blaster;
            w.gunRange = s.gunRange;
            w.burstInterval = s.burstInterval;
            w.burstShots = s.burstShots;
            w.shotSpacing = s.shotSpacing;
            w.spread = s.spread;
            w.fireArc = s.fireArc;
            w.holdFire = 0f;
            w.weaponsFree = false;
            var v = go.AddComponent<GroundVehicle>();
            v.surface = surface;
            v.body = body;
            v.turret = turret;
            Hull(go, kit, s);
            Identity(go, s, TargetInfo.Kind.Fighter);
            go.SetActive(true);
            return go;
        }

        /// <summary>A torpedo station on the sea floor: a dome on a footing, its tubes round it.</summary>
        static GameObject SeaBase(SurfaceKit kit, Transform area, Vector3 ground, float yaw)
        {
            if (kit.torpedo == null) return null;
            var s = Stats.seaBase;
            var go = new GameObject("Sea Base");
            go.SetActive(false);
            go.transform.SetParent(area, false);
            // (its middle is the dome's, what guns aim at: clear of the sea bed it stands on)
            go.transform.SetPositionAndRotation(ground + Vector3.up * 12f, Quaternion.Euler(0f, yaw, 0f));
            Part(PrimitiveType.Cylinder, go.transform, "Footing", new Vector3(0f, -9f, 0f), new Vector3(64f, 3f, 64f), kit.bunker);
            Part(PrimitiveType.Sphere, go.transform, "Dome", new Vector3(0f, -5f, 0f), new Vector3(50f, 28f, 50f), kit.armor);
            Part(PrimitiveType.Cylinder, go.transform, "Mast", new Vector3(0f, 10f, 0f), new Vector3(3f, 6f, 3f), kit.bunker);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f + 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Part(PrimitiveType.Cylinder, go.transform, "Torpedo tube", dir * 26f - Vector3.up * 5f, new Vector3(5f, 7f, 5f), kit.bunker,
                     new Vector3(60f, a, 0f));
            }
            Light(go.transform, new Vector3(0f, 17f, 0f), kit.warning, 90f);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -1f, 0f);
            col.size = new Vector3(64f, 22f, 64f);
            var w = go.AddComponent<EnemyWeapons>();
            w.missilePrefab = kit.torpedo;
            w.torpedoes = true;
            w.missileRange = s.missileRange;
            w.salvoInterval = s.salvoInterval;
            w.salvoSize = s.salvoSize;
            w.holdFire = 0f;
            w.weaponsFree = false;
            Hull(go, kit, s);
            Identity(go, s, TargetInfo.Kind.Capital);
            go.AddComponent<GroundUnit>().sonar = true;
            go.SetActive(true);
            return go;
        }

        /// <summary>An attack submarine, cruising between the sea floor and the surface.</summary>
        static GameObject Submarine(SurfaceKit kit, Transform area, PlanetSurface surface, Vector3 ground, float sea, float yaw)
        {
            if (kit.torpedo == null) return null;
            var s = Stats.submarine;
            var go = new GameObject("Attack Submarine");
            go.SetActive(false);
            go.transform.SetParent(area, false);
            float depth = Mathf.Max(s.keelClearance, (sea - ground.y) * 0.5f);
            go.transform.SetPositionAndRotation(ground + Vector3.up * depth, Quaternion.Euler(0f, yaw, 0f));
            Part(PrimitiveType.Capsule, go.transform, "Hull", Vector3.zero, new Vector3(9f, 30f, 9f), kit.bunker, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, go.transform, "Sail", new Vector3(0f, 6.5f, 9f), new Vector3(3.6f, 7f, 11f), kit.bunker);
            Part(PrimitiveType.Cube, go.transform, "Sail planes", new Vector3(0f, 7f, 10f), new Vector3(13f, 0.5f, 3f), kit.armor);
            Part(PrimitiveType.Cube, go.transform, "Stern planes", new Vector3(0f, 0f, -25f), new Vector3(17f, 0.6f, 4f), kit.armor);
            Part(PrimitiveType.Cube, go.transform, "Rudder", new Vector3(0f, 3.5f, -27f), new Vector3(0.6f, 7f, 4f), kit.armor);
            Light(go.transform, new Vector3(0f, 10.5f, 10f), kit.warning, 40f);
            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.radius = 5f;
            col.height = 60f;
            var w = go.AddComponent<EnemyWeapons>();
            w.missilePrefab = kit.torpedo;
            w.torpedoes = true;
            w.missileRange = s.missileRange;
            w.salvoInterval = s.salvoInterval;
            w.salvoSize = s.salvoSize;
            w.holdFire = 0f;
            w.weaponsFree = false;
            var sub = go.AddComponent<Submarine>();
            sub.surface = surface;
            sub.cruiseSpeed = s.cruiseSpeed;
            sub.attackSpeed = s.attackSpeed;
            sub.turnRate = s.turnRate;
            sub.standoff = s.standoff;
            sub.keelClearance = s.keelClearance;
            Hull(go, kit, s);
            Identity(go, s, TargetInfo.Kind.Fighter);
            go.AddComponent<GroundUnit>().sonar = true;
            go.SetActive(true);
            return go;
        }

        /// <summary>A fighter (or a gunship) on patrol in the sky, or parked on a pad until the alarm.</summary>
        static GameObject Flyer(GameObject prefab, Transform area, Vector3 pos, float yaw, string display, bool parked, float gunship)
        {
            if (prefab == null) return null;
            var s = gunship > 0f ? Stats.gunship : Stats.fighter;
            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), area);
            go.name = display;
            var ti = go.GetComponent<TargetInfo>();
            if (ti != null) ti.displayName = display;
            var h = go.GetComponent<Health>();
            if (h != null) h.respawnDelay = 0f;
            var w = go.GetComponent<EnemyWeapons>();
            if (w != null) { w.holdFire = 0f; w.weaponsFree = false; w.fireArc = s.fireArc; }
            var pilot = go.AddComponent<CombatPilot>();
            pilot.concealed = false;
            pilot.startLurking = true;
            pilot.leashDistance = s.leashDistance;
            pilot.hideBelow = 0f;
            pilot.cruiseSpeed = s.cruiseSpeed; pilot.attackSpeed = s.attackSpeed; pilot.turnRate = s.turnRate; pilot.acceleration = s.acceleration;
            if (gunship > 0f)
            {
                pilot.style = CombatPilot.Style.Capital;
                pilot.standoff = s.standoff; pilot.circleSpeed = s.circleSpeed;
            }
            else
            {
                pilot.style = CombatPilot.Style.Fighter;
                pilot.breakOffDistance = s.breakOffDistance; pilot.extendDistance = s.extendDistance;
                pilot.loiterRadius = parked ? 0f : s.loiterRadius;
            }
            if (parked)
            {
                pilot.enabled = false;
                var launch = go.AddComponent<PadLaunch>();
                launch.pilot = pilot;
                launch.climb = s.climb;
            }
            return go;
        }
    }
}
