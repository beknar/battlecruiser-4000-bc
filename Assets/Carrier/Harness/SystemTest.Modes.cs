using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The modes of play (-modestest): the number keys change mode, each mode's keys work in it
    /// and not in the others, the controls box shows the mode's keys, and a mode's menu opens
    /// over a game that goes on. Combat: the menu lists the targets in range and targets the one
    /// chosen, and lining up turns the ship until its guns bear.
    /// </summary>
    public partial class SystemTest
    {
        bool modesOnly;

        static IEnumerator Keys(Keyboard kb, params K[] keys)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            yield return null;
        }

        IEnumerator ModesTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            var hud = FindAnyObjectByType<ShipHud>();
            var combat = demo.Combat;
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;
            Check(Modes.Current == ModeOfPlay.Combat, $"a game starts in combat mode ({Modes.Name(Modes.Current)})");

            // ---- the number keys, and the controls box for each mode
            var keys = new[] { (K.Digit2, ModeOfPlay.Mining, "mineral scan"), (K.Digit3, ModeOfPlay.Navigation, "system map"),
                               (K.Digit4, ModeOfPlay.Services, "services"), (K.Digit5, ModeOfPlay.Fleet, "fleet yet"),
                               (K.Digit1, ModeOfPlay.Combat, "fire guns") };
            foreach (var (key, mode, line) in keys)
            {
                yield return Key(kb, key);
                string help = hud != null ? hud.HelpText(false) : "";
                string pad = hud != null ? hud.HelpText(true) : "";
                Check(Modes.Current == mode && help.Contains($"{Modes.Name(mode)} MODE") && help.Contains(line) && pad.Contains($"{Modes.Name(mode)} MODE"),
                      $"{key}: {Modes.Name(Modes.Current)} mode, its keys in the controls box ('{line}')");
            }
            Shot("modes_combat");

            // ---- modes taken up by themselves unwind in turn: mining, a site (services), the map
            // open there (navigation); the map closed: services; the site left: mining
            Modes.Set(ModeOfPlay.Mining);
            Modes.Set(ModeOfPlay.Services, auto: true);
            Modes.Set(ModeOfPlay.Navigation, auto: true);
            Modes.Return(ModeOfPlay.Navigation);
            var afterMap = Modes.Current;
            Modes.Return(ModeOfPlay.Services);
            Check(afterMap == ModeOfPlay.Services && Modes.Is(ModeOfPlay.Mining),
                  $"modes taken up by themselves unwind in turn: the map closed, {Modes.Name(afterMap)}; the site left, {Modes.Name(Modes.Current)}");
            // and a mode the player chose meanwhile stays
            Modes.Set(ModeOfPlay.Services, auto: true);
            Modes.Set(ModeOfPlay.Fleet);
            Modes.Return(ModeOfPlay.Services);
            Check(Modes.Is(ModeOfPlay.Fleet), $"a mode chosen meanwhile stays when the site is left ({Modes.Name(Modes.Current)})");
            yield return Key(kb, K.Digit1);

            // ---- a mode's keys work only in it: V scans in mining mode, not in combat mode
            var sensor = demo.Sensor;
            if (sensor != null)
            {
                yield return new WaitForSeconds(sensor.Sweeping ? 3f : 0f);
                yield return Key(kb, K.V);
                bool inCombat = sensor.Sweeping;
                yield return Key(kb, K.Digit2);
                yield return Key(kb, K.V);
                bool inMining = sensor.Sweeping;
                Check(!inCombat && inMining, $"V scans in mining mode (sweeping {inMining}), not in combat mode ({inCombat})");
                yield return Key(kb, K.Digit1);
            }

            // ---- services mode, away from any site: says where to go
            yield return Key(kb, K.Digit4);
            int notices = Notices.Count;
            yield return Key(kb, K.Tab);
            Check(Notices.Count > notices && (Notices.Last ?? "").Contains("dock at a station") && !ModeMenu.IsOpen,
                  $"services mode's Tab, away from any site, says to dock or land ('{Notices.Last}')");
            yield return Key(kb, K.Digit1);

            // ---- combat: a target to choose from the menu
            cc.throttle = 0.3f;
            var dummy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dummy.name = "Practice target";
            dummy.transform.localScale = Vector3.one * 60f;
            dummy.SetActive(false);
            var dh = dummy.AddComponent<Health>();
            dh.maxHealth = 1e9f;
            dh.cannotDie = true;
            var info = dummy.AddComponent<TargetInfo>();
            info.displayName = "Practice target";
            info.shipClass = "Target drone";
            dummy.GetComponent<Renderer>().enabled = false;
            dummy.SetActive(true);
            // astern and a little to port: few guns, or none, bear on it as the ship is
            var t = cc.transform;
            dummy.transform.position = t.position - t.forward * 1800f - t.right * 400f;
            Targets.Add(dummy.transform);
            cc.SetTarget(null);
            yield return null;

            yield return Key(kb, K.Tab);
            var menu = ModeMenu.Instance;
            bool listed = ModeMenu.IsOpen && menu != null && menu.Rows.Any(r => (Object)(r.tag as Transform) == dummy.transform);
            Check(listed, $"Tab opens the combat menu, listing the targets in range ({(menu != null ? menu.Rows.Count(r => !r.header) : 0)} listed)");
            Vector3 p0 = cc.transform.position;
            float t0 = Time.time;
            yield return new WaitForSeconds(1f);
            Check(Time.timeScale > 0.5f && Vector3.Distance(cc.transform.position, p0) > 10f,
                  $"the game goes on under the menu (time scale {Time.timeScale:F1}, the ship moved {Vector3.Distance(cc.transform.position, p0):F0} m in {Time.time - t0:F1} s)");
            Shot("modes_combat_menu");
            for (int i = 0; i < 20 && menu != null && (Object)(menu.Selected?.tag as Transform) != dummy.transform; i++) yield return Key(kb, K.DownArrow);
            yield return Key(kb, K.Enter);
            Check(cc.target == dummy.transform, $"Enter targets the one chosen ({(cc.target != null ? cc.target.name : "none")})");
            yield return Key(kb, K.Tab);
            Check(!ModeMenu.IsOpen, "Tab closes it");

            // ---- lining up
            float before = CombatMode.BestTurn(cc, dummy.transform.position, out int bestBear, out int guns);
            int bearing0 = cc.mainTurrets.Count(g => g != null && g.CanBear(dummy.transform.position));
            float heading0 = cc.Heading;
            yield return Key(kb, K.C);
            Check(combat != null && combat.LiningUp, "C lines up on the target");
            // (the target stays where it was put; the ship turns)
            float until = Time.time + 30f;
            int bearing = 0;
            while (Time.time < until && combat.LiningUp)
            {
                bearing = cc.mainTurrets.Count(g => g != null && g.CanBear(dummy.transform.position));
                if (bearing >= bestBear && bestBear > 0 && Mathf.Abs(CombatMode.BestTurn(cc, dummy.transform.position, out _, out _)) < 8f) break;
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            bearing = cc.mainTurrets.Count(g => g != null && g.CanBear(dummy.transform.position));
            Check(bearing >= bestBear && bearing > bearing0,
                  $"the ship turns {Mathf.DeltaAngle(heading0, cc.Heading):F0} deg and its guns bear: {bearing0} -> {bearing} of {guns} (best {bestBear}, a turn of {before:F0} deg)");
            Shot("modes_lined_up");
            // a flight key takes the helm back
            InputSystem.QueueStateEvent(kb, new KeyboardState(K.A));
            yield return new WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Check(!combat.LiningUp && demo.autoRudder == null, "steering takes the helm back from the line-up");

            // ---- Shift+T and T step through the targets
            yield return Keys(kb, K.LeftShift, K.T);
            Check(cc.target != null, $"Shift+T targets one too ({(cc.target != null ? cc.target.name : "none")})");

            Targets.Remove(dummy.transform);
            Destroy(dummy);
            cc.throttle = 0f;

            // ---- salvage: a hostile destroyed leaves a wreck; chosen in the menu, Z strips it into the hold
            yield return SalvageTest(kb, Check);
            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: modes of play ({passed} of {checks})");
        }

        IEnumerator SalvageTest(Keyboard kb, System.Action<bool, string> Check)
        {
            var salvage = Salvage.Instance;
            var hold = demo.Cargo;
            if (salvage == null || hold == null) { Check(false, "salvage: no salvage or no hold"); yield break; }
            int made0 = salvage.WrecksMade;
            var t = cc.transform;
            var prey = new GameObject("Practice hulk");
            prey.SetActive(false);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(prey.transform, false);
            box.transform.localScale = new Vector3(40f, 20f, 120f);
            Object.Destroy(box.GetComponent<Collider>());
            var ph = prey.AddComponent<Health>();
            ph.maxHealth = 500f;
            var pi = prey.AddComponent<TargetInfo>();
            pi.displayName = "Practice hulk";
            pi.shipClass = "Target hulk";
            prey.transform.position = t.position + t.forward * 700f + t.right * 200f;
            prey.SetActive(true);
            // (kept: the hulk is gone once destroyed, its transform with it)
            var preyAt = prey.transform;
            Targets.Add(preyAt);
            yield return null;
            ph.Damage(1e6f);
            yield return null;
            var wreck = Wreck.All.LastOrDefault();
            Check(salvage.WrecksMade == made0 + 1 && wreck != null && wreck.Of == "Practice hulk" && !wreck.Empty,
                  $"a hostile destroyed leaves a wreck: {(wreck != null ? $"{wreck.Name}, {wreck.Summary()} ({wreck.Tonnes:F0} t)" : "none")}");
            Targets.Remove(preyAt);
            if (wreck == null) { if (prey != null) Destroy(prey); yield break; }

            // chosen from the combat menu
            yield return Key(kb, K.Tab);
            var menu = ModeMenu.Instance;
            Check(menu != null && menu.Rows.Any(r => r.header && r.text.StartsWith("WRECKS")) && menu.Rows.Any(r => (Object)(r.tag as Wreck) == wreck),
                  "the combat menu lists it under WRECKS");
            for (int i = 0; i < 20 && menu != null && (Object)(menu.Selected?.tag as Wreck) != wreck; i++) yield return Key(kb, K.DownArrow);
            yield return Key(kb, K.Enter);
            Check(salvage.Chosen == wreck, $"Enter chooses it ({(salvage.Chosen != null ? salvage.Chosen.Name : "none")}); '{Notices.Last}'");
            yield return Key(kb, K.Tab);
            Shot("modes_wreck");

            // Z, in reach: the beam strips it into the hold
            float steel0 = hold.Amount("SteelPlate");
            yield return Key(kb, K.Z);
            Check(salvage.Working == wreck, $"Z starts the salvage beam ('{Notices.Last}')");
            yield return new WaitForSeconds(1.5f);
            Shot("modes_salvaging");
            float until = Time.time + 15f;
            while (Time.time < until && salvage.Working != null) yield return null;
            Check(hold.Amount("SteelPlate") > steel0 && salvage.Salvaged > 0f && (wreck == null || wreck.Empty),
                  $"it is stripped into the hold: steel plate {steel0:F0} -> {hold.Amount("SteelPlate"):F0} t, {salvage.Salvaged:F0} t salvaged ('{Notices.Last}')");
            if (prey != null) Destroy(prey);
        }
    }
}
