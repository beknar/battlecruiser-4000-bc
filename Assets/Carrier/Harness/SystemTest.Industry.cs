using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -industrytest (?industrytest): the refineries and factories. With the lines sped up, a
    /// whole chain: ore taken to the Belt Smelter (docked on its pad on the rock), stored and
    /// refined into steel plate and aluminium sheet, which go in the hold to the Kestrel Deep Yard,
    /// where a cargo pod is built and fitted (the hold grows) and taken off again. The gas works
    /// (a refinery only), and landing on the pads of Ochre Foundry and Mnemos Works, where the
    /// refinery and factory stand beside the pad. And the save written and read back.
    /// </summary>
    public partial class SystemTest
    {
        bool industryOnly;

        IEnumerator IndustryTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            var table = IndustryTable.Get();
            var panel = IndustryPanel.Instance;
            var director = IndustryDirector.Instance;
            var hold = cc.GetComponent<CargoHold>();
            var fit = cc.GetComponent<ShipFittings>();
            IndustrySave.TestPath = Path.Combine(dir, "industry_save.json");
            if (File.Exists(IndustrySave.TestPath)) File.Delete(IndustrySave.TestPath);
            Check(panel != null && director != null && hold != null && fit != null,
                  $"the sites' screen, the industry and the frigate's hold and part slots ({(fit != null ? fit.slots : 0)} slots, {(hold != null ? hold.capacity : 0f):F0} t)");
            if (panel == null || director == null || hold == null || fit == null) yield break;
            var missing = table.sites.Where(s => Point(s.name) == null).Select(s => s.name).ToArray();
            Check(missing.Length == 0, $"every industrial site is a destination on the map ({table.sites.Length - missing.Length} of {table.sites.Length}{(missing.Length > 0 ? "; missing " + string.Join(", ", missing) : "")})");
            IndustryLedger.SpeedUp = 40f;
            var dock = Docking.Instance;
            yield return Key(kb, K.H);   // (the controls list off the pictures)

            // dock at a site in space (or on the asteroid's pad): the screen opens by itself
            IEnumerator DockAt(string name, System.Action<bool> done)
            {
                var st = Installation.All.FirstOrDefault(i => i.displayName == name);
                if (st == null) { done(false); yield break; }
                string file = name.Split(' ')[0].ToLower();
                yield return WarpTo(kb, name, "iw_" + file);
                yield return new WaitForSeconds(1f);
                yield return LookAt(st.transform.position, $"i_{file}_outside", 2.2f, 8f);
                // (from above, so as not to fly into the rock)
                yield return FlyTo(st.BerthPosition + Vector3.up * 500f, st.dockRange * 0.8f, 60f);
                cc.throttle = 0f;
                yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
                var can = dock.Candidate(out string why);
                Log($"  {name}: {Vector3.Distance(cc.transform.position, st.BerthPosition):F0} m from the berth; can dock at {can?.displayName ?? "nothing"}{(why != "" ? $" ({why})" : "")}");
                yield return Key(kb, K.K);
                float until = Time.time + 10f;
                while (Time.time < until && !(dock.Current == Docking.State.Docked && IndustryPanel.IsOpen)) yield return null;
                yield return new WaitForSeconds(1.5f);
                done(dock.Current == Docking.State.Docked && dock.At == st && IndustryPanel.IsOpen && panel.Site != null && panel.Site.siteName == name);
            }
            IEnumerator Leave()
            {
                if (IndustryPanel.IsOpen) yield return Key(kb, K.Escape);
                yield return new WaitForSeconds(0.3f);
                yield return Key(kb, K.K);
                float until = Time.time + 6f;
                while (Time.time < until && dock.Current != Docking.State.Free) yield return null;
                yield return new WaitForSeconds(1f);
                cc.throttle = 0f;
            }
            IEnumerator Wait(System.Func<bool> done, float seconds)
            {
                float until = Time.time + seconds;
                while (Time.time < until && !done()) yield return null;
            }

            // ---- the smelter: ore in, stored, refined, the goods loaded
            bool ok = false;
            yield return DockAt("Belt Smelter", r => ok = r);
            float keel = cc.transform.position.y - IndustrySite.All.First(s => s.siteName == "Belt Smelter").transform.position.y;
            Check(ok, $"docks at the Belt Smelter, down on its pad on the rock, and its screen opens ({panel.Current})");
            if (!ok) { Log($"FAIL: the industry ({passed} of {checks})"); yield break; }
            Shot("i_smelter_docked");
            hold.Restore(null, hold.Credits);
            hold.Add(Mineral.Iron, 14f);
            hold.Add(Mineral.Carbon, 2f);
            hold.Add(Mineral.Aluminium, 32f);
            var smelter = IndustryLedger.Site("Belt Smelter").stock;
            // "Store everything aboard" (the second row), with Enter
            panel.Show(IndustryPanel.Page.Storage);
            yield return Key(kb, K.DownArrow);
            yield return Key(kb, K.Enter);
            yield return null;
            Check(hold.Used < 0.01f && smelter.Amount("Iron") >= 14f - 1e-3f && smelter.Amount("Aluminium") >= 32f - 1e-3f,
                  $"Enter on 'store everything' puts the hold in storage (hold {hold.Used:F1} t; stored iron {smelter.Amount("Iron"):F0} t, aluminium {smelter.Amount("Aluminium"):F0} t)");
            Shot("i_storage");
            // to the refinery page (Tab), and queue: steel plate twice, aluminium sheet six times
            yield return Key(kb, K.Tab);
            Check(panel.Current == IndustryPanel.Page.Refine, $"Tab goes to the refinery page ({panel.Current})");
            panel.SelectItem("SteelPlate");
            yield return Key(kb, K.Enter);
            yield return Key(kb, K.Enter);
            panel.SelectItem("AluminiumSheet");
            panel.ActOnSelected(IndustryPanel.Action("queue5"));
            yield return Key(kb, K.Enter);
            var line = IndustryLedger.Site("Belt Smelter").refining;
            Check(line.Count == 8 && smelter.Amount("Iron") < 4.01f + 1e-3f, $"eight jobs queued, their ore taken from storage ({line.Count} queued; iron left {smelter.Amount("Iron"):F1} t)");
            Shot("i_refining");
            yield return Wait(() => line.Count == 0, 30f);
            float steel = smelter.Amount("SteelPlate"), sheet = smelter.Amount("AluminiumSheet");
            Check(line.Count == 0 && steel >= 10f - 1e-3f && sheet >= 27f - 1e-3f, $"the refinery works through its queue: {steel:F1} t of steel plate, {sheet:F1} t of aluminium sheet");
            // back to storage, and into the hold
            panel.Show(IndustryPanel.Page.Storage);
            foreach (var id in new[] { "SteelPlate", "AluminiumSheet" }) { panel.SelectItem(id); panel.ActOnSelected(IndustryPanel.Action("loadAll")); }
            Check(hold.Amount("SteelPlate") >= 10f - 1e-3f && hold.Amount("AluminiumSheet") >= 27f - 1e-3f && smelter.Amount("SteelPlate") < 1e-3f,
                  $"loaded aboard: {hold.Amount("SteelPlate"):F1} t of steel plate and {hold.Amount("AluminiumSheet"):F1} t of sheet; the hold {hold.Used:F0} of {hold.capacity:F0} t");
            yield return Leave();
            Check(dock.Current == Docking.State.Free, $"Esc closes the screen, K lifts off the pad ({dock.Current}, {keel:F0} m above the rock's site)");
            {
                var hp = cc.GetComponent<Health>();
                var sh = cc.GetComponentInChildren<Shield>();
                Log($"  after lifting off: hull {hp.Current:F0}, shield {(sh != null ? sh.Strength : 0f):F0}, last hit {Time.time - Mathf.Max(hp.LastHitTime, sh != null ? sh.LastHitTime : -100f):F1} s ago; " +
                    $"warp {(drive.CanWarp(out string why) ? "ready" : why)}");
                // (a bump on the pad locks the warp drive for a few seconds: wait it out)
                float clear = Time.time + drive.recentHitSeconds + 1f;
                while (!drive.CanWarp(out _) && Time.time < clear) yield return null;
            }

            // ---- the deep yard: build a cargo pod and fit it
            yield return DockAt("Kestrel Deep Yard", r => ok = r);
            Check(ok, "docks at the Kestrel Deep Yard, and its screen opens");
            if (ok)
            {
                Shot("i_yard_docked");
                var yard = IndustryLedger.Site("Kestrel Deep Yard").stock;
                panel.Show(IndustryPanel.Page.Storage);
                panel.SelectItem("*all");
                panel.ActOnSelected(IndustryPanel.Action("storeAll"));
                yield return Key(kb, K.Tab);
                Check(panel.Current == IndustryPanel.Page.Build, $"a factory only: Tab goes straight to the factory page ({panel.Current})");
                panel.SelectItem("CargoPod");
                yield return Key(kb, K.Enter);
                Shot("i_building");
                yield return Wait(() => yard.Amount("CargoPod") >= 1f, 20f);
                Check(yard.Amount("CargoPod") >= 1f && yard.Amount("AluminiumSheet") < 2.01f, $"a cargo pod built from 25 t of sheet and 10 t of steel ({yard.Amount("CargoPod"):F0} built)");
                float cap0 = hold.capacity;
                yield return Key(kb, K.Tab);
                Check(panel.Current == IndustryPanel.Page.Fit, $"then the fitting page ({panel.Current})");
                panel.SelectItem("CargoPod");
                yield return Key(kb, K.Enter);
                float factor = table.Part("CargoPod").effects[0].factor;
                Check(fit.IsFitted("CargoPod") && Mathf.Abs(hold.capacity - cap0 * factor) < 0.5f && yard.Amount("CargoPod") < 1f,
                      $"fitted: the hold grows {cap0:F0} -> {hold.capacity:F0} t, a slot taken ({fit.Fitted.Count} of {fit.slots})");
                Shot("i_fitted");
                yield return Key(kb, K.Enter);
                Check(!fit.IsFitted("CargoPod") && Mathf.Abs(hold.capacity - cap0) < 0.5f && yard.Amount("CargoPod") >= 1f,
                      $"taken off again: the hold back to {hold.capacity:F0} t, the pod in storage");
                yield return Key(kb, K.Enter);   // (and fitted again: it's saved fitted)
                yield return Leave();
            }

            // ---- the gas works: a refinery only, in orbit round Jovara
            yield return DockAt("Jovara Gas Works", r => ok = r);
            if (ok)
            {
                var spec = panel.Site.Spec;
                Check(spec.CanRefine && !spec.CanBuild && spec.Refines("FusionPellets") && !spec.Refines("SteelPlate"),
                      $"the gas works refines gases only, and builds nothing ({string.Join(", ", spec.refines)})");
                yield return Key(kb, K.Tab);
                Shot("i_gasworks");
                yield return Leave();
            }
            else Check(false, "docks at the Jovara Gas Works");

            // ---- on the ground: Ochre Foundry and Mnemos Works, round their pads
            var landing = PlanetLanding.Instance;
            foreach (var name in new[] { "Ochre Foundry", "Mnemos Works" })
            {
                yield return WarpTo(kb, name, "iw_" + name.Split(' ')[0].ToLower());
                yield return new WaitForSeconds(1f);
                string file = name.Split(' ')[0].ToLower();
                yield return LookAt(Point(name).Position, $"i_{file}_from_orbit", 2.4f, 40f);
                yield return Key(kb, K.K);
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 120f);
                float downBy = Time.time + 120f;
                while (landing.Autopilot && Time.time < downBy) yield return null;
                yield return new WaitForSeconds(1f);
                var site = IndustrySite.All.FirstOrDefault(s => s.siteName == name && s.pad != null);
                var surface = landing.Surface;
                bool refinery = surface != null && surface.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Ind_RefinerySurface");
                bool factory = surface != null && surface.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Ind_FactorySurface");
                Check(landing.Current == PlanetLanding.State.OnSurface && site != null && refinery && factory,
                      $"down at {name}: its pad{(site != null ? $" {Vector3.Distance(Flat(site.pad.position), Flat(cc.transform.position)):F0} m off" : " missing")}, refinery {refinery}, factory {factory}");
                if (site == null) { landing.TakeOff(); yield return WaitLanding(landing, PlanetLanding.State.InSpace, 15f); continue; }
                Shot($"i_{file}_surface");
                yield return LookAt(site.transform.position, $"i_{file}_site", 4f, 14f);
                // over to the pad, and down on it
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
                yield return Wait(() => IndustryPanel.IsOpen, 4f);
                Check(landing.Landed && landing.OnPad && IndustryPanel.IsOpen && panel.Site == site,
                      $"sets down on {name}'s pad (landed {landing.Landed}, on the pad {landing.OnPad}) and its screen opens ({IndustryPanel.IsOpen})");
                Shot($"i_{file}_screen");
                if (IndustryPanel.IsOpen) yield return Key(kb, K.Escape);
                yield return new WaitForSeconds(0.5f);
                var oc = demo.orbitCamera;
                if (oc != null)
                {
                    float d0 = oc.distance, p0 = oc.pitch;
                    oc.distance = Mathf.Min(oc.maxDistance, 1600f);
                    oc.pitch = 22f;
                    yield return new WaitForSeconds(0.8f);
                    Shot($"i_{file}_on_the_pad");
                    oc.distance = d0; oc.pitch = p0;
                }
                landing.TakeOff();
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 15f);
                yield return new WaitForSeconds(0.5f);
            }

            // ---- the save: everything written, and read back as it was
            bool saved = director.Save();
            var back = IndustrySave.Read(IndustrySave.TestPath);
            var yardBack = back?.sites.FirstOrDefault(s => s.name == "Kestrel Deep Yard");
            var fitBack = back?.fittings.FirstOrDefault(f => f.frigate == FrigateClass.Of(cc));
            Check(saved && back != null && yardBack != null && fitBack != null && fitBack.parts.Contains("CargoPod"),
                  $"the game saved ({IndustrySave.Saves} saves to {Path.GetFileName(IndustrySave.TestPath)}): {back?.sites.Count ?? 0} sites, " +
                  $"fitted {(fitBack != null ? string.Join(", ", fitBack.parts) : "-")}, {back?.credits ?? 0f:N0} cr");
            IndustryLedger.SpeedUp = 1f;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: the industry ({passed} of {checks})");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
