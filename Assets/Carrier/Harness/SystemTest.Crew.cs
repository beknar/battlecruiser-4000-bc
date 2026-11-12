using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -crewtest (?crewtest): crew and the settlements. Hull damage costs the frigate crew; more
    /// than 10% lost and it slows (top speed, turning, shield, guns); repairs don't bring them
    /// back. Every installation has its crew but the automated ones; a hostile one's guns slow as
    /// it loses crew. Then each of the ten settlements: warped to and looked at, docked at (or
    /// landed on, on its pad), and crew recruited there for credits; and the pause menu no longer
    /// offers another frigate.
    /// </summary>
    public partial class SystemTest
    {
        bool crewOnly;

        static readonly string[] SpaceSettlements = { "Concord Colony", "Arges Station", "Hollow Rock", "Lantern Arcology", "Aegis Fleet Base", "Ochre Orbital Works" };
        static readonly string[] GroundSettlements = { "Port Ochre", "Pyrois Domes", "Fort Brontes", "Steropes Heavy Industries" };

        /// <summary>The warp brought the frigate to a place (within its arrival distance, give or take).</summary>
        bool Arrived(string name)
        {
            var wp = Point(name);
            return wp != null && Vector3.Distance(cc.transform.position, wp.Position) < wp.arrivalDistance * 1.6f;
        }

        IEnumerator CrewTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            var crew = cc.GetComponent<Crew>();
            var hull = cc.GetComponent<Health>();
            var hold = cc.GetComponent<CargoHold>();
            var panel = IndustryPanel.Instance;
            var dock = Docking.Instance;
            var landing = PlanetLanding.Instance;
            var table = CrewTable.Get();
            yield return Key(kb, K.H);
            Check(crew != null && crew.Aboard == crew.complement && crew.Efficiency == 1f, $"the frigate has its full crew ({(crew != null ? crew.Aboard : 0)} of {(crew != null ? crew.complement : 0)})");
            if (crew == null || panel == null) { Log($"FAIL: crew ({passed} of {checks})"); yield break; }

            // ---- the pause menu: no other frigate
            var menu = FindAnyObjectByType<GameMenu>();
            bool shipItem = false;
            if (menu != null)
            {
                menu.Open();
                yield return null;
                for (int i = 0; i < 8; i++) { shipItem |= menu.SelectedLabel.StartsWith("Ship"); yield return Key(kb, K.DownArrow); }
                menu.Close();
                yield return null;
            }
            Check(menu != null && !shipItem, "the pause menu offers no other frigate");

            // ---- losses, and what they do
            float speed0 = cc.maxSpeed, turn0 = cc.turnRate;
            var shield = cc.GetComponentInChildren<Shield>();
            float shield0 = shield != null ? shield.capacity : 0f;
            var gun = cc.mainTurrets != null ? cc.mainTurrets.FirstOrDefault(t => t != null) : null;
            float interval0 = gun != null ? gun.fireInterval : 0f;
            hull.cannotDie = true;
            hull.Damage(hull.maxHealth * 0.15f);
            Check(crew.Aboard < crew.complement && crew.Aboard >= crew.complement * 0.9f - 1 && Mathf.Approximately(cc.maxSpeed, speed0),
                  $"a light hit costs a few crew ({crew.Aboard} of {crew.complement}) and nothing else yet (top speed {cc.maxSpeed:F0} m/s)");
            hull.Damage(hull.maxHealth * 0.6f);
            float eff = crew.Efficiency;
            Check(eff < 1f && cc.maxSpeed < speed0 - 0.5f && cc.turnRate < turn0 && (shield == null || shield.capacity < shield0) && (gun == null || gun.fireInterval > interval0),
                  $"more than 10% lost ({crew.Aboard} of {crew.complement}): working at {eff:P0}; top speed {speed0:F0} -> {cc.maxSpeed:F0} m/s, " +
                  $"turn {turn0:F0} -> {cc.turnRate:F0}, shield {shield0:F0} -> {(shield != null ? shield.capacity : 0f):F0}, gun every {interval0:F2} -> {(gun != null ? gun.fireInterval : 0f):F2} s");
            hull.Repair();
            Check(crew.Aboard < crew.complement * 0.9f, $"repairs mend the hull, not the crew ({crew.Aboard} aboard)");
            hull.cannotDie = false;
            Shot("c_console");
            // (the hits count as being under fire for a while: no warping till it has passed)
            yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);

            // ---- the installations' crews
            var all = Installation.All.Where(i => i != null).ToList();
            var automated = all.Where(i => table.CrewOf(i.displayName) == 0).ToList();
            var crewed = all.Where(i => i.GetComponent<Crew>() != null).ToList();
            Check(automated.Count >= 3 && automated.All(i => i.GetComponent<Crew>() == null) && crewed.Count >= all.Count - automated.Count,
                  $"every installation has its crew ({crewed.Count}) but the automated ones ({string.Join(", ", automated.Select(i => i.displayName))})");
            var haven = all.FirstOrDefault(i => i.displayName == "Corsair Haven");
            var havenCrew = haven != null ? haven.GetComponent<Crew>() : null;
            if (havenCrew != null)
            {
                var guns = haven.GetComponentsInChildren<EnemyWeapons>(true);
                havenCrew.Lose(havenCrew.complement * 0.5f);
                Check(guns.Length > 0 && guns.All(g => g.efficiency < 1f), $"a hostile base's guns slow as it loses crew ({havenCrew.Aboard} of {havenCrew.complement}: {havenCrew.Efficiency:P0})");
                havenCrew.SetAboard(havenCrew.complement);
            }
            else Check(false, "Corsair Haven has a crew");

            // ---- the settlements: there, docked or landed, crew recruited
            var missing = SpaceSettlements.Concat(GroundSettlements).Where(n => Point(n) == null).ToArray();
            Check(missing.Length == 0, $"the ten settlements are on the map{(missing.Length > 0 ? ": missing " + string.Join(", ", missing) : "")}");
            IEnumerator RecruitHere(string name)
            {
                if (!IndustryPanel.IsOpen) panel.Open(IndustryPanel.Page.Crew);
                yield return null;
                panel.Show(IndustryPanel.Page.Crew);
                hold.Restore(hold.Contents(), table.recruitCost * 12f);
                if (crew.Missing < 10) crew.Lose(15f);   // (room for ten)
                int before = crew.Aboard;
                panel.SelectItem("*r10");
                yield return Key(kb, K.Enter);
                Shot($"c_{name.Split(' ')[0].ToLower()}_crew");
                Check(crew.Aboard == Mathf.Min(crew.complement, before + 10) && hold.Credits < table.recruitCost * 2.5f + 0.5f,
                      $"{name}: ten recruits sign on ({before} -> {crew.Aboard}), {table.recruitCost * 10f:N0} cr paid; {IndustryLedger.Pool(name):F0} still waiting there");
                yield return Key(kb, K.Escape);
                yield return new WaitForSeconds(0.3f);
            }
            foreach (var name in SpaceSettlements)
            {
                var st = Installation.All.FirstOrDefault(i => i.displayName == name);
                if (st == null) { Check(false, $"{name} is there to dock at"); continue; }
                string file = name.Split(' ')[0].ToLower();
                yield return WarpTo(kb, name, "cw_" + file);
                yield return new WaitForSeconds(1f);
                if (!Arrived(name)) { Check(false, $"arrives at {name}"); continue; }
                yield return LookAt(st.transform.position, $"c_{file}_outside", 2.6f, 8f);
                yield return FlyTo(st.BerthPosition + Vector3.up * 400f, st.dockRange * 0.8f, 60f);
                cc.throttle = 0f;
                yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
                // (only K when there is somewhere to dock: with nowhere, K would land instead)
                if (dock.Candidate(out string why) != st) { Check(false, $"docks at {name} ({(why != "" ? why : "out of range")})"); continue; }
                yield return Key(kb, K.K);
                float until = Time.time + 10f;
                while (Time.time < until && !(dock.Current == Docking.State.Docked && IndustryPanel.IsOpen)) yield return null;
                yield return new WaitForSeconds(1.2f);
                bool docked = dock.Current == Docking.State.Docked && dock.At == st && IndustryPanel.IsOpen;
                Check(docked, $"docks at {name}, and its screen opens ({dock.Current}, open {IndustryPanel.IsOpen})");
                if (docked) yield return RecruitHere(name);
                if (IndustryPanel.IsOpen) yield return Key(kb, K.Escape);
                yield return Key(kb, K.K);
                until = Time.time + 6f;
                while (Time.time < until && dock.Current != Docking.State.Free) yield return null;
                yield return new WaitForSeconds(1f);
                cc.throttle = 0f;
            }
            foreach (var name in GroundSettlements)
            {
                string file = name.Split(' ')[0].ToLower();
                yield return WarpTo(kb, name, "cw_" + file);
                yield return new WaitForSeconds(1f);
                if (!Arrived(name)) { Check(false, $"arrives over {name}"); continue; }
                yield return LookAt(Point(name).Position, $"c_{file}_from_orbit", 2.4f, 40f);
                yield return Key(kb, K.K);
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 60f);
                yield return new WaitForSeconds(2.5f);
                var site = IndustrySite.All.FirstOrDefault(x => x.siteName == name && x.pad != null);
                var group = landing.Surface != null ? landing.Surface.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Settlement: " + name) : null;
                int buildings = group != null ? group.childCount - 1 : 0;
                Check(site != null && buildings >= 5, $"down at {name}: its pad {(site != null ? "there" : "missing")}, {buildings} buildings round it");
                if (site == null) { yield return Key(kb, K.K); yield return WaitLanding(landing, PlanetLanding.State.InSpace, 15f); continue; }
                yield return LookAt(site.transform.position + Vector3.up * 80f, $"c_{file}_site", 5f, 16f);
                float until = Time.time + 90f;
                bool descending = false;
                while (Time.time < until && !landing.Landed)
                {
                    Vector3 to = site.pad.position - cc.transform.position;
                    to.y = 0f;
                    float d = to.magnitude;
                    var v = ShipAutopilot.BowOn(cc, site.pad.position + Vector3.up * (cc.transform.position.y - site.pad.position.y));
                    demo.autoRudder = d > 60f ? v.x : 0f;
                    demo.autoPitch = 0f;
                    cc.throttle = d > 400f ? 0.5f : d > 200f ? 0.25f : d > 60f ? 0.1f : 0f;
                    if (!descending && d < 90f && cc.Speed < 12f) descending = true;
                    demo.autoElevator = descending ? -1f : 0f;
                    yield return null;
                }
                demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
                cc.throttle = 0f;
                float wait = Time.time + 4f;
                while (Time.time < wait && !IndustryPanel.IsOpen) yield return null;
                Check(landing.Landed && landing.OnPad && IndustryPanel.IsOpen, $"sets down on {name}'s pad (landed {landing.Landed}, on the pad {landing.OnPad}), its screen opens ({IndustryPanel.IsOpen})");
                if (IndustryPanel.IsOpen) yield return RecruitHere(name);
                var oc = demo.orbitCamera;
                if (oc != null)
                {
                    float d0 = oc.distance, p0 = oc.pitch;
                    oc.distance = Mathf.Min(oc.maxDistance, 2200f);
                    oc.pitch = 24f;
                    yield return new WaitForSeconds(0.8f);
                    Shot($"c_{file}_on_the_pad");
                    oc.distance = d0; oc.pitch = p0;
                }
                yield return Key(kb, K.K);
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 15f);
                yield return new WaitForSeconds(0.5f);
            }
            Log($"{(passed == checks ? "PASS" : "FAIL")}: crew ({passed} of {checks})");
        }
    }
}
