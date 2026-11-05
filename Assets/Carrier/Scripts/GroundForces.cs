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
            bool gunship = false;
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
                    case UnitKind.Patrol:
                        go = Flyer(kit.fighter, area, ground + Vector3.up * 380f, yaw, "Patrol Fighter", false, 0f);
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

        static Health Hull(GameObject go, SurfaceKit kit, float hp, float boom)
        {
            if (!go.TryGetComponent(out Health h)) h = go.AddComponent<Health>();   // its events come from Health.Awake
            h.maxHealth = hp;
            h.respawnDelay = 0f;
            h.deathEffect = boom > 0.8f || kit.explosionSmall == null ? kit.explosion : kit.explosionSmall;
            h.deathEffectScale = boom;
            return h;
        }

        /// <summary>A heavy triple turret on a bunker.</summary>
        static GameObject Battery(SurfaceKit kit, Transform area, Vector3 ground, float yaw)
        {
            if (kit.battery == null) return null;
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
            t.range = 4500f;
            t.fireInterval = 2.4f;
            t.fireCone = 4f;
            t.leadTarget = true;
            t.yawLimit = 180f;
            t.minPitch = -5f;
            t.maxPitch = 80f;
            t.yawSpeed = 35f;
            t.pitchSpeed = 30f;
            t.autoFire = true;
            t.target = null;
            var w = go.AddComponent<EnemyWeapons>();
            w.turrets = new[] { t };
            w.holdFire = 0f;
            w.weaponsFree = false;
            Hull(go, kit, 900f, 1.3f);
            Identity(go, "Ground Battery", "Heavy gun emplacement", "Triple cannon turret", "Medium", TargetInfo.Kind.Capital);
            go.SetActive(true);
            return go;
        }

        /// <summary>A bunker with a box of missile cells raised on it.</summary>
        static GameObject MissileSite(SurfaceKit kit, Transform area, Vector3 ground, float yaw)
        {
            if (kit.missile == null) return null;
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
            w.missileRange = 6500f;
            w.salvoInterval = 11f;
            w.salvoSize = 3;
            w.holdFire = 0f;
            w.weaponsFree = false;
            Hull(go, kit, 650f, 1.1f);
            Identity(go, "Missile Site", "Surface-to-air missile battery", "Guided missiles", "High", TargetInfo.Kind.Capital);
            go.SetActive(true);
            return go;
        }

        /// <summary>A hover tank: a wedge hull on skirts with a turret and a long gun.</summary>
        static GameObject Tank(SurfaceKit kit, Transform area, PlanetSurface surface, Vector3 ground, float yaw)
        {
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
            w.gunRange = 3200f;
            w.burstInterval = 3.5f;
            w.burstShots = 2;
            w.shotSpacing = 0.25f;
            w.spread = 1.2f;
            w.fireArc = 180f;
            w.holdFire = 0f;
            w.weaponsFree = false;
            var v = go.AddComponent<GroundVehicle>();
            v.surface = surface;
            v.body = body;
            v.turret = turret;
            Hull(go, kit, 380f, 0.7f);
            Identity(go, "Hover Tank", "Armoured hover tank", "Cannon", "Medium", TargetInfo.Kind.Fighter);
            go.SetActive(true);
            return go;
        }

        /// <summary>A fighter (or a gunship) on patrol in the sky, or parked on a pad until the alarm.</summary>
        static GameObject Flyer(GameObject prefab, Transform area, Vector3 pos, float yaw, string display, bool parked, float gunship)
        {
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), area);
            go.name = display;
            var ti = go.GetComponent<TargetInfo>();
            if (ti != null) ti.displayName = display;
            var h = go.GetComponent<Health>();
            if (h != null) h.respawnDelay = 0f;
            var w = go.GetComponent<EnemyWeapons>();
            if (w != null) { w.holdFire = 0f; w.weaponsFree = false; w.fireArc = gunship > 0f ? 180f : 20f; }
            var pilot = go.AddComponent<CombatPilot>();
            pilot.concealed = false;
            pilot.startLurking = true;
            pilot.leashDistance = 11000f;
            pilot.hideBelow = 0f;
            if (gunship > 0f)
            {
                pilot.style = CombatPilot.Style.Capital;
                pilot.cruiseSpeed = 100f; pilot.attackSpeed = 130f; pilot.turnRate = 18f; pilot.acceleration = 25f;
                pilot.standoff = 1800f; pilot.circleSpeed = 4f;
            }
            else
            {
                pilot.style = CombatPilot.Style.Fighter;
                pilot.cruiseSpeed = 200f; pilot.attackSpeed = 290f; pilot.turnRate = 85f; pilot.acceleration = 140f;
                pilot.breakOffDistance = 420f; pilot.extendDistance = 1300f;
                pilot.loiterRadius = parked ? 0f : 700f;
            }
            if (parked)
            {
                pilot.enabled = false;
                var launch = go.AddComponent<PadLaunch>();
                launch.pilot = pilot;
                launch.climb = gunship > 0f ? 320f : 240f;
            }
            return go;
        }
    }

    /// <summary>One area's ground forces: they all go to action stations together when one of
    /// them sees the player's ship within `spotRange` (hills hide it), or one of them is hit; and
    /// stand down when it has gone `leash` away.</summary>
    public class GroundGarrison : MonoBehaviour, IOriginShift
    {
        public PlanetSurface surface;
        public readonly List<GroundUnit> units = new List<GroundUnit>();
        public Vector3 center;
        public float spotRange = 5500f;
        public float leash = 12000f;

        public bool Alert { get; private set; }
        public int TimesAlerted { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lastToast = -100f; }
        static float lastToast = -100f;

        Transform player;
        float nextLook;

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (var u in units) if (u != null && u.Alive) n++;
                return n;
            }
        }

        void Start()
        {
            var cc = FindAnyObjectByType<CarrierController>();
            if (cc != null) player = cc.transform;
        }

        public void OriginShifted(Vector3 d) => center += d;

        void Update()
        {
            if (player == null) return;
            units.RemoveAll(u => u == null);
            if (Time.time < nextLook) return;
            nextLook = Time.time + 0.3f;
            Vector3 pp = player.position;
            if (!Alert)
            {
                bool spotted = false;
                foreach (var u in units)
                {
                    if (!u.Alive) continue;
                    if (u.WasHit) { spotted = true; break; }
                    Vector3 e = u.Eye;
                    if ((e - pp).sqrMagnitude < spotRange * spotRange && Cover.Visible(e, pp)) { spotted = true; break; }
                }
                if (spotted) Raise();
            }
            else if ((center - pp).sqrMagnitude > leash * leash)
            {
                Alert = false;
                foreach (var u in units) u.StandDown();
            }
        }

        public void Raise()
        {
            if (Alert) return;
            Alert = true;
            TimesAlerted++;
            foreach (var u in units) u.Engage();
            if (Time.time - lastToast > 10f)
            {
                lastToast = Time.time;
                SoundFX.Ui(Sfx.UiAlert);
                Demo.CarrierDemoInput.Toast("Ground defences have spotted us!");
            }
        }
    }

    /// <summary>One of the ground forces: a target for the player, remembered when destroyed.</summary>
    public class GroundUnit : MonoBehaviour
    {
        public long key;
        public GroundGarrison garrison;
        public PlanetSurface surface;

        Health health;
        EnemyWeapons weapons;
        CombatPilot pilot;
        PadLaunch launch;
        GroundVehicle vehicle;
        int hitsSeen;

        public bool Alive => health == null || !health.Dead;
        public bool WasHit => health != null && health.Hits > hitsSeen;
        /// <summary>Where it looks out from (above its own hull, so the ground it stands on doesn't block the view).</summary>
        public Vector3 Eye => transform.position + Vector3.up * 12f;

        void Start()
        {
            health = GetComponent<Health>();
            weapons = GetComponent<EnemyWeapons>();
            pilot = GetComponent<CombatPilot>();
            launch = GetComponent<PadLaunch>();
            vehicle = GetComponent<GroundVehicle>();
            if (health != null)
            {
                hitsSeen = health.Hits;
                health.onDestroyed.AddListener(() => { if (surface != null) surface.Destroyed.Add(key); });
            }
            Demo.CarrierDemoInput.AddTarget(transform);
        }

        void OnDestroy() => Demo.CarrierDemoInput.RemoveTarget(transform);

        public void Engage()
        {
            if (!Alive) return;
            if (weapons != null) { weapons.weaponsFree = true; weapons.OpenFireSoon(1.2f); }
            if (launch != null && !launch.Launched) launch.Launch();
            else if (pilot != null && pilot.enabled) pilot.Engage();
            if (vehicle != null) vehicle.engaged = true;
        }

        public void StandDown()
        {
            if (health != null) hitsSeen = health.Hits;
            if (weapons != null) weapons.weaponsFree = false;
            if (pilot != null && pilot.enabled) pilot.Disengage();
            if (vehicle != null) vehicle.engaged = false;
        }
    }

    /// <summary>A ship parked on a launch pad: when the alarm goes up it lifts straight off, then
    /// its pilot takes over (and comes back to hover over the pad when the fight is over).</summary>
    public class PadLaunch : MonoBehaviour, IOriginShift
    {
        public CombatPilot pilot;
        public float climb = 240f;
        public float climbTime = 4.5f;
        public bool Launched { get; private set; }
        bool launching;

        public void Launch()
        {
            if (Launched || launching) return;
            launching = true;
            StartCoroutine(Lift());
        }

        Vector3 start;

        IEnumerator Lift()
        {
            var h = GetComponent<Health>();
            start = transform.position;
            SoundFX.Play(Sfx.WarpExit, start, 0.5f, 1.4f);
            for (float t = 0f; t < climbTime; t += Time.deltaTime)
            {
                if (h != null && h.Dead) yield break;
                float k = t / climbTime;
                transform.position = start + Vector3.up * climb * (k * k * (3f - 2f * k));
                yield return null;
            }
            transform.position = start + Vector3.up * climb;
            launching = false;
            Launched = true;
            if (pilot != null)
            {
                pilot.SetHome(transform.position, transform.rotation);
                pilot.enabled = true;
                pilot.Engage();
            }
        }

        public void OriginShifted(Vector3 d) => start += d;
    }

    /// <summary>
    /// A hover tank: it circles its post until the alarm, then drives toward the player's ship
    /// and circles it at gun range, riding a few metres over the ground (and over water and lava
    /// alike), leaning with the slope, its turret on the target.
    /// </summary>
    public class GroundVehicle : MonoBehaviour, IOriginShift, IVelocity
    {
        public PlanetSurface surface;
        public Transform body, turret;
        public float hover = 3.5f;
        public float patrolSpeed = 10f, chaseSpeed = 24f;
        public float patrolRadius = 350f, standoff = 1400f;
        public float turnRate = 40f;
        public bool engaged;

        public Vector3 Velocity { get; private set; }

        Vector3 home;
        float angle, speed, heading, circleSign = 1f;
        Transform target;
        Health health;

        void Start()
        {
            home = transform.position;
            heading = transform.eulerAngles.y;
            angle = Random.Range(0f, Mathf.PI * 2f);
            circleSign = Random.value < 0.5f ? -1f : 1f;
            health = GetComponent<Health>();
            var cc = FindAnyObjectByType<CarrierController>();
            if (cc != null) target = cc.transform;
        }

        public void OriginShifted(Vector3 d) => home += d;

        void Update()
        {
            if (surface == null || (health != null && health.Dead)) return;
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 goal;
            float want;
            if (engaged && target != null)
            {
                Vector3 to = target.position - pos;
                to.y = 0f;
                float d = to.magnitude;
                if (d > standoff) { goal = target.position; want = chaseSpeed; }
                else
                {
                    // circle at gun range
                    Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * circleSign;
                    goal = pos + side * 200f - to.normalized * (standoff - d) * 0.5f;
                    want = chaseSpeed * 0.7f;
                }
            }
            else
            {
                angle += patrolSpeed / patrolRadius * dt * circleSign;
                goal = home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * patrolRadius;
                want = patrolSpeed;
            }
            Vector3 dir = goal - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1f)
            {
                float wantHeading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                heading = Mathf.MoveTowardsAngle(heading, wantHeading, turnRate * dt);
            }
            speed = Mathf.MoveTowards(speed, want, 8f * dt);
            Vector3 fwd = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector3 next = pos + fwd * speed * dt;
            float floor = surface.FloorAt(next);
            next.y = Mathf.Lerp(pos.y, floor + hover, 1f - Mathf.Exp(-6f * dt));
            next.y = Mathf.Max(next.y, floor + 1f);
            Velocity = (next - pos) / Mathf.Max(dt, 1e-4f);
            transform.position = next;
            // lean with the ground
            float f = surface.FloorAt(next + fwd * 8f), b = surface.FloorAt(next - fwd * 8f);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float r = surface.FloorAt(next + right * 6f), l = surface.FloorAt(next - right * 6f);
            Vector3 n = new Vector3(-(r - l) / 12f, 1f, -(f - b) / 16f);
            Vector3 normal = (right * n.x + Vector3.up + fwd * n.z).normalized;
            var flat = Quaternion.Euler(0f, heading, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.FromToRotation(Vector3.up, normal) * flat, 1f - Mathf.Exp(-5f * dt));
            if (turret != null && target != null && engaged)
            {
                Vector3 t = turret.parent.InverseTransformDirection(target.position - turret.position);
                float yaw = Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg;
                turret.localRotation = Quaternion.RotateTowards(turret.localRotation, Quaternion.Euler(0f, yaw, 0f), 60f * dt);
            }
        }
    }

    /// <summary>A warning light that blinks.</summary>
    public class Blink : MonoBehaviour
    {
        UnityEngine.Light l;
        Renderer r;
        float phase;
        void Start() { l = GetComponentInChildren<UnityEngine.Light>(); r = GetComponent<Renderer>(); phase = Random.value; }
        void Update()
        {
            bool on = Mathf.Repeat(Time.time * 0.8f + phase, 1f) < 0.5f;
            if (l != null) l.enabled = on;
            if (r != null) r.enabled = on;
        }
    }
}
