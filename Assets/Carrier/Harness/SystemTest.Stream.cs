using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -streamtest (?streamtest): what is loaded when, and what goes on when nothing is. The heavy
    /// places (the settlements, the industrial sites) start unloaded; warping to one loads its
    /// looks, warping far away lets them go again and the memory they took with them. A gun
    /// battery destroyed on Cinder stays destroyed when the frigate lands there again, and is
    /// rebuilt in its time though the surface was never there meanwhile; the world's memory is in
    /// the save.
    /// </summary>
    public partial class SystemTest
    {
        bool streamOnly;

        static string Memory() =>
            $"{Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB allocated, textures {Texture.currentTextureMemory / 1048576} MB, " +
            $"{Resources.FindObjectsOfTypeAll<Mesh>().Length} meshes";

        IEnumerator StreamTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            yield return Key(kb, K.H);
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;   // (Cinder's guns are not under test)
            var all = StreamedVisual.All.Where(v => v != null).ToList();
            yield return new WaitForSeconds(5f);       // (the start's own loads and clear-outs settle)
            var loaded = all.Where(v => v.Loaded).Select(v => v.name).ToArray();
            Check(all.Count >= 15 && loaded.Length <= 3,
                  $"{all.Count} heavy places stream their looks; at the start {loaded.Length} loaded ({string.Join(", ", loaded)}); {Memory()}");
            ulong tex0 = Texture.currentTextureMemory;

            // ---- there: loaded; far away: let go
            var lantern = all.FirstOrDefault(v => v.name == "Lantern Arcology");
            if (lantern == null) { Check(false, "the Lantern Arcology streams its looks"); }
            else
            {
                yield return WarpTo(kb, "Lantern Arcology", "st_lantern");
                yield return new WaitForSeconds(1.5f);
                int renderers = lantern.GetComponentsInChildren<Renderer>().Length;
                ulong tex1 = Texture.currentTextureMemory;
                Check(lantern.Loaded && renderers > 0, $"arriving at the Lantern Arcology, its looks are loaded ({renderers} renderers; {Memory()})");
                yield return LookAt(lantern.transform.position, "st_lantern_loaded", 3f, 10f);
                yield return WarpTo(kb, "Kestrel", "st_away");
                yield return new WaitForSeconds(7f);
                ulong tex2 = Texture.currentTextureMemory;
                Check(!lantern.Loaded && lantern.GetComponentsInChildren<Renderer>().Length == 0 && tex2 < tex1,
                      $"far away, they are let go, and their textures with them ({tex1 / 1048576} -> {tex2 / 1048576} MB, {tex0 / 1048576} at the start; {Memory()})");
            }

            // ---- the ground forces: destroyed, still destroyed, rebuilt in their time
            var landing = PlanetLanding.Instance;
            var fortress = Point("Cinder Fortress");
            long key = 0;
            float minutes = 0f;
            IEnumerator Land()
            {
                if (landing.Current != PlanetLanding.State.InSpace) { landing.TakeOff(); yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f); }
                landing.LandOn(fortress);
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 90f);
                yield return new WaitForSeconds(3f);
            }
            GroundUnit Unit(long k) => landing.Surface != null ? landing.Surface.GetComponentsInChildren<GroundUnit>(true).FirstOrDefault(u => u.key == k) : null;
            yield return Land();
            var target = landing.Surface != null
                ? landing.Surface.GetComponentsInChildren<GroundUnit>(true).Where(u => u.Alive && u.rebuildMinutes > 0f && u.GetComponent<Health>() != null)
                                 .OrderBy(u => Vector3.Distance(u.transform.position, cc.transform.position)).FirstOrDefault()
                : null;
            if (target == null) Check(false, "a ground unit to destroy on Cinder");
            else
            {
                key = target.key;
                minutes = target.rebuildMinutes;
                string what = target.name;
                var h = target.GetComponent<Health>();
                h.Damage(h.maxHealth * 10f);
                yield return new WaitForSeconds(1.5f);
                var mem = landing.Surface.Destroyed;
                Check(mem.Contains(key), $"a {what} destroyed on Cinder is remembered, to be rebuilt in {minutes:F0} min");
                yield return Land();
                Check(Unit(key) == null, $"landing there again, it is still destroyed ({landing.Surface.GetComponentsInChildren<GroundUnit>(true).Length} units built)");
                // the save: the world's clock and memory
                IndustrySave.TestPath = Path.Combine(dir, "world_save.json");
                bool saved = IndustryDirector.Instance != null && IndustryDirector.Instance.Save();
                var back = IndustrySave.Read(IndustrySave.TestPath);
                var cinder = back?.world?.bodies?.FirstOrDefault(b => b.name == "Cinder");
                Check(saved && cinder != null && cinder.units.Contains(key) && back.world.clock > 0.0,
                      $"the save holds the world: its clock ({back?.world?.clock ?? 0:F0} s) and Cinder's destroyed and explored ({cinder?.units.Count ?? 0}, {cinder?.explored.Count ?? 0})");
                // time passes with nobody there
                landing.TakeOff();
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);
                WorldState.Advance(minutes * 60f + 5f);
                yield return Land();
                var again = Unit(key);
                Check(again != null && again.Alive, $"{minutes:F0} min later, landing again, it has been rebuilt ({(again != null ? again.name : "not there")})");
                Shot("st_rebuilt");
                landing.TakeOff();
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);
            }
            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: streaming and the world ({passed} of {checks})");
        }
    }
}
