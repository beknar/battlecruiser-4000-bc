using System.Collections;
using System.Linq;
using CarrierGame.Demo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The frigates (-shiptest, with -frigate &lt;id&gt; for one other than the starting frigate):
    /// the level put the chosen one in and pointed everything at it; it has what every frigate
    /// must (its turrets, a CIWS mount or more, one main gun, none on a destroyer); its guns fire
    /// on a target ahead, to each side, above, below and astern, never off target or through the hull;
    /// its main gun charges and fires; a destroyer's or cruiser's missiles fire from its cells
    /// and its magazine fills again. Pictures of it from round about, and of the start menu.
    /// </summary>
    public partial class SystemTest
    {
        bool shipOnly;

        IEnumerator ShipTest(Keyboard kb)
        {
            var roster = FrigateRoster.Get();
            string want = FrigateChoice.Current;
            string flying = FrigateClass.Of(cc);
            var entry = roster != null ? roster.Find(flying) : null;
            Log($"frigate: asked for '{(string.IsNullOrEmpty(want) ? "(the starting one)" : want)}', flying the {entry?.displayName ?? flying} ({cc.shipClass})");
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log((ok ? "ok: " : "FAIL: ") + what);
            }

            // the swap: the chosen class, and the level's parts all on it
            Check(string.IsNullOrEmpty(want) ? flying == roster?.Default?.id : string.Equals(flying, want, System.StringComparison.OrdinalIgnoreCase),
                  "the level flies the chosen frigate");
            var ships = FindObjectsByType<CarrierController>(FindObjectsSortMode.None).Where(c => c.GetComponent<FrigateClass>() != null).ToArray();
            Check(ships.Length == 1, $"one player frigate in the level ({ships.Length})");
            var landing = FindAnyObjectByType<PlanetLanding>();
            var docking = FindAnyObjectByType<Docking>();
            var cam = FindAnyObjectByType<OrbitCamera>();
            Check(demo.carrier == cc && map.carrier == cc && (landing == null || landing.carrier == cc) && (docking == null || docking.carrier == cc)
                  && cam != null && cam.target == cc.transform, "the controls, map, landing, docking and camera are on it");
            Check(drive != null && drive.streakMaterial != null && map.drive == drive, "it has the level's warp drive");

            // what every frigate has
            var st = FrigateStats.Of(cc.gameObject);
            Log($"  {st.turrets} turrets ({st.barrels} barrels, {st.gunDps:F0} damage/s, range {st.gunRange:F0} m), {st.ciws} CIWS, {st.mainGuns} main gun, " +
                $"hull {st.hull:F0}, shield {st.shield:F0}, {st.speed:F0} m/s, {st.turnRate:F0} deg/s, hold {st.cargo:F0} t");
            var proto = entry != null ? FrigateStats.Of(entry.prefab) : st;
            Check(st.turrets >= 2 && st.turrets == proto.turrets && cc.mainTurrets.All(t => t != null && t.muzzles.Length > 0),
                  $"its turrets, each with its muzzles ({st.turrets} turrets, {st.barrels} barrels)");
            Check(st.ciws >= 1, $"at least one CIWS mount ({st.ciws})");
            bool destroyer = entry != null && entry.category == FrigateRoster.Destroyer;
            Check(st.mainGuns == (destroyer ? 0 : 1), destroyer ? $"no main gun on a destroyer ({st.mainGuns})" : $"one main gun ({st.mainGuns})");
            if (entry != null && entry.category != FrigateRoster.Frigate)
                Check(st.missiles > 0 && st.missileCells > 0 && cc.missiles != null && cc.missiles.Stock == st.missiles,
                      $"missiles: {st.missiles} in the magazine, {st.missileCells} cells");

            Shot("ship_" + flying + "_start");
            yield return Pictures(flying);

            // the guns: a target ahead, to each side, above, below and astern
            cc.throttle = 0f;
            var dummy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dummy.name = "Gunnery target";
            dummy.transform.localScale = Vector3.one * 60f;
            // (its strength set before it wakes: Health fills up to its maximum in Awake)
            dummy.SetActive(false);
            var dh = dummy.AddComponent<Health>();
            dh.maxHealth = 1e9f;
            dh.cannotDie = true;
            dummy.GetComponent<Renderer>().enabled = false;   // (a sphere with no material in a build: magenta)
            dummy.SetActive(true);
            var t = cc.transform;
            var places = new (string name, Vector3 at)[]
            {
                ("ahead", t.forward * 1600f),
                ("to port", -t.right * 1500f + t.forward * 300f),
                ("to starboard", t.right * 1500f + t.forward * 300f),
                ("above", t.up * 1200f + t.forward * 500f),
                ("below", -t.up * 1200f + t.forward * 500f),   // (for the keel mounts)
                ("astern", -t.forward * 1600f),
            };
            int bearing = 0, intoHull = 0;
            var restLines = cc.mainTurrets.ToDictionary(x => x, x => BarrelLines(x));
            foreach (var turret in cc.mainTurrets) turret.autoFire = true;
            var everFired = cc.mainTurrets.ToDictionary(x => x, x => false);
            foreach (var (name, at) in places)
            {
                dummy.transform.position = t.position + at;
                cc.SetTarget(dummy.transform);
                var before = cc.mainTurrets.ToDictionary(x => x, x => x.ShotsFired);
                int hits0 = dh.Hits;
                yield return new WaitForSeconds(5f);
                var fired = cc.mainTurrets.Where(x => x.ShotsFired > before[x]).ToArray();
                foreach (var f in fired) everFired[f] = true;
                if (fired.Length > 0) bearing++;
                // no barrel pointing into the ship (beyond any already touching it at rest)
                var into = cc.mainTurrets.Where(x => BarrelLines(x).Zip(restLines[x], (now, rest) => rest && !now).Any(b => b)).Select(x => x.Label).ToArray();
                intoHull += into.Length;
                Log($"  target {name}: {fired.Length} of {cc.mainTurrets.Length} turrets fired ({string.Join(", ", fired.Select(f => f.Label))}), {dh.Hits - hits0} hits" +
                    (into.Length > 0 ? $"; pointing into the hull: {string.Join(", ", into)}" : ""));
                if (name == "ahead") Shot("ship_" + flying + "_firing");
                if (name == "to port" || name == "to starboard") yield return Overhead($"ship_{flying}_aim_{name.Replace("to ", "")}");
            }
            cc.SetTarget(null);
            int off = cc.mainTurrets.Sum(x => x.OffTargetShots), through = cc.mainTurrets.Sum(x => x.HullShots);
            Check(bearing >= 3, $"its guns bear ahead, to the sides, above or astern ({bearing} of {places.Length} places)");
            Check(everFired.Values.All(v => v), $"every turret fired at something ({everFired.Values.Count(v => v)} of {everFired.Count})");
            Check(dh.Hits > 0, "the shots hit");
            Check(off == 0 && through == 0, $"no shot off target or through the hull ({off} off target, {through} through the hull)");
            Check(intoHull == 0, $"no turret points into its own hull ({intoHull})");

            // the main gun
            var gun = cc.GetComponentInChildren<MainBeam>();
            dummy.transform.position = t.position + t.forward * 2500f;
            cc.SetTarget(dummy.transform);
            if (gun != null)
            {
                float dmg0 = dh.TotalDamage;
                demo.ChargeMainGun();
                yield return new WaitForSeconds(gun.chargeTime + gun.fireDuration + 1.5f);
                Check(dh.TotalDamage > dmg0, $"the main gun charges ({gun.chargeTime:F0} s) and hits ({dh.TotalDamage - dmg0:F0} damage)");
            }

            // missiles (a destroyer's or the cruiser's): a salvo from the cells, one fewer aboard
            // for each, and the magazine filled again
            if (cc.missiles != null && cc.missiles.magazine > 0)
            {
                foreach (var turret in cc.mainTurrets) turret.autoFire = false;
                int stock0 = cc.missiles.Stock, ready = cc.missiles.ReadyCells;
                float dmg0 = dh.TotalDamage;
                demo.FireMissiles();
                yield return new WaitForSeconds(ready * cc.missiles.salvoInterval + 9f);
                int fired = stock0 - cc.missiles.Stock;
                Log($"  missiles: {fired} fired of {ready} cells ready, {cc.missiles.Stock} left, {dh.TotalDamage - dmg0:F0} damage");
                Check(fired > 0 && fired <= ready, $"a missile salvo from the cells ({fired} fired, {ready} cells)");
                Check(dh.TotalDamage > dmg0, "the missiles hit");
                Check(cc.missiles.Rearm() == fired && cc.missiles.Stock == cc.missiles.magazine, "rearming fills the magazine");
            }
            cc.SetTarget(null);
            Destroy(dummy);

            // the start menu, shown for the picture
            var menu = FindAnyObjectByType<StartMenu>();
            if (menu != null)
            {
                menu.Open();
                yield return new WaitForSecondsRealtime(1.5f);
                Shot("ship_menu_" + flying);
                yield return new WaitForSecondsRealtime(0.5f);
                Check(StartMenu.IsOpen && Time.timeScale == 0f, "the start menu opens over the game and pauses it");
                menu.Close();
                yield return null;
                Check(!StartMenu.IsOpen && Time.timeScale > 0f, "and closes again");
            }
            Log($"{(passed == checks ? "PASS" : "FAIL")}: the {entry?.displayName ?? flying} ({passed} of {checks})");
        }

        /// <summary>Whether each of a turret's gun lines (its breech along each barrel to just past the
        /// muzzle) is clear of the ship's hull.</summary>
        bool[] BarrelLines(CarrierTurret t)
        {
            var hull = cc.transform.Find("HullCollider");
            var cols = hull != null ? hull.GetComponents<Collider>() : new Collider[0];
            var from = t.pitch != null ? t.pitch.position : t.yaw.position;
            return t.muzzles.Select(m =>
            {
                Vector3 to = m.position + m.forward * 1.5f, d = to - from;
                float len = d.magnitude;
                if (len < 0.01f) return true;
                d /= len;
                return !cols.Any(c => c.Raycast(new Ray(from, d), out _, len) || c.Raycast(new Ray(to, -d), out _, len));
            }).ToArray();
        }

        /// <summary>A picture from above the ship, close, with the HUD off: where the turrets point.</summary>
        IEnumerator Overhead(string name)
        {
            var cam = FindAnyObjectByType<OrbitCamera>();
            if (cam == null) yield break;
            var hud = HudParts();
            foreach (var b in hud) b.enabled = false;
            float yaw0 = cam.yaw, pitch0 = cam.pitch, dist0 = cam.distance;
            cam.yaw = cc.transform.eulerAngles.y + 180f;
            cam.pitch = 62f;
            cam.distance = demo.ShipRadius * 1.15f;
            yield return new WaitForSeconds(0.6f);
            Shot(name);
            yield return new WaitForSeconds(0.3f);
            cam.yaw = yaw0;
            cam.pitch = pitch0;
            cam.distance = dist0;
            foreach (var b in hud) b.enabled = true;
        }

        MonoBehaviour[] HudParts() => demo.GetComponents<MonoBehaviour>().Where(b => b != null && b.enabled && b != demo &&
            (b is ShipHud || b is TargetDisplay || b is MiningDisplay || b is ShipConsole || b is CaptainPortrait || b is CoopWorld)).ToArray();

        /// <summary>The ship from round about, close up on the level's own camera, with the HUD off.</summary>
        IEnumerator Pictures(string flying)
        {
            var cam = FindAnyObjectByType<OrbitCamera>();
            if (cam == null) yield break;
            float r = demo.ShipRadius;
            var views = new (string name, float yaw, float pitch, float dist)[]
            {
                ("bow", 35f, 18f, 1.25f), ("beam", 95f, 10f, 1.3f), ("quarter", 150f, 25f, 1.25f), ("below", 60f, -25f, 1.3f),
            };
            var hud = demo.GetComponents<MonoBehaviour>().Where(b => b != null && b.enabled && b != demo &&
                (b is ShipHud || b is TargetDisplay || b is MiningDisplay || b is ShipConsole || b is CaptainPortrait || b is CoopWorld)).ToArray();
            foreach (var b in hud) b.enabled = false;
            float yaw0 = cam.yaw, pitch0 = cam.pitch, dist0 = cam.distance;
            foreach (var v in views)
            {
                cam.yaw = cc.transform.eulerAngles.y + v.yaw;
                cam.pitch = v.pitch;
                cam.distance = r * v.dist;
                yield return new WaitForSeconds(0.6f);
                Shot($"ship_{flying}_{v.name}");
                yield return new WaitForSeconds(0.3f);
            }
            cam.yaw = yaw0;
            cam.pitch = pitch0;
            cam.distance = dist0;
            foreach (var b in hud) b.enabled = true;
        }
    }
}
