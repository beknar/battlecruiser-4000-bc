using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -hudtest (or ?hudtest): the ship's console and the captain. The console is in the bottom-
    /// left corner with its bars and lights, and the old status text is gone from the top left;
    /// all five captains have all their faces. Then the ship goes through what can happen to it,
    /// and the captain must react to each: a hit on the shield, a hull hit, the bow gun charging
    /// and firing, a scan, a warp, docking, a landing, a fight at Corsair Haven (contact, hits,
    /// guns firing, a kill), the ship lost and back, and a message spoken. Last, a captain is
    /// chosen by clicking her portrait in the pause menu, and every captain is pictured. The
    /// player's own choice of captain is put back at the end.
    /// </summary>
    public partial class SystemTest
    {
        bool hudOnly;

        IEnumerator HudTest(Keyboard kb, Mouse mouse)
        {
            var results = new List<(string, bool)>();
            var cap = CaptainPortrait.Instance;
            var hull = cc.GetComponent<Health>();
            var shield = cc.GetComponentInChildren<Shield>(true);
            int chosen0 = Captain.Index;
            if (cap == null) { Log("FAIL: no captain on the HUD"); yield break; }
            yield return new WaitForSeconds(3f);

            // ---- the console
            var r = ShipConsole.Rect;
            bool corner = r.width > 200f && r.xMin < 40f && Screen.height - r.yMax < 40f && r.yMin > Screen.height * 0.3f;
            var help = CarrierDemoInput.HelpRect;
            Log($"console at ({r.xMin:F0}, {r.yMin:F0}) {r.width:F0} x {r.height:F0} on a {Screen.width} x {Screen.height} screen; " +
                $"top-left panel {help.width:F0} x {help.height:F0} (the controls only); alerts '{ShipConsole.Alerts}'");
            Shot("h0_console");
            results.Add(("the console is in the bottom-left corner", corner && (help.height == 0f || help.yMax < r.yMin)));

            // ---- the captains' faces
            int[] found = Captain.Roster.Select((c, i) => Captain.FacesFound(i)).ToArray();
            int moods = Enum.GetValues(typeof(Mood)).Length;
            Log($"faces: {string.Join(", ", Captain.Roster.Select((c, i) => $"{c.name} {found[i]}/{moods}"))}");
            results.Add(("five captains, every face", Captain.Roster.Length == 5 && found.All(n => n == moods)));

            // ---- reactions
            yield return Settle(cap);
            Log($"at rest: {cap.Mood} '{cap.Reason}'");
            results.Add(("calm when all is quiet", cap.Mood == Mood.Neutral));

            // a hit on the shield, then one on the hull
            hull.Damage(250f, cc.transform.position + cc.transform.forward * 60f, HitKind.Cannon);
            yield return Expect(cap, Mood.Alarmed, "SHIELD", 1f, "h1_shield_hit", results, "alarmed when the shield is hit");
            yield return Settle(cap);
            hull.Damage(500f);
            yield return Expect(cap, Mood.Pain, "HULL", 1f, "h2_hull_hit", results, "wincing when the hull is hit");
            hull.Repair();
            yield return Settle(cap);

            // the bow gun: intent while it charges, set-jawed as it fires
            demo.ChargeMainGun();
            yield return Expect(cap, Mood.Focused, "CHARGING", 2f, "h3_charging", results, "intent while the bow gun charges");
            yield return Expect(cap, Mood.Determined, "MAIN GUN", 14f, "h4_firing", results, "determined as the bow gun fires");
            yield return Settle(cap);

            // a scan
            demo.MineralScan();
            yield return Expect(cap, Mood.Focused, "SCANNING", 2f, "h5_scan", results, "intent while scanning");
            yield return Settle(cap);

            // a warp: awed in the tunnel, pleased to arrive
            int seen = cap.History.Count;
            yield return WarpTo(kb, "Thalassa Highport", "h6_warp");
            bool awe = cap.History.Skip(seen).Any(h => h.mood == Mood.Awe);
            bool arrived = cap.History.Skip(seen).Any(h => h.mood == Mood.Smile && h.reason == "ARRIVED");
            Log($"  warp: {Moods(cap, seen)}");
            results.Add(("awed in warp, pleased to arrive", awe && arrived));

            // docking: repaired and pleased
            var dock = Docking.Instance;
            seen = cap.History.Count;
            var hp = Installation.All.FirstOrDefault(i => i.displayName == "Thalassa Highport");
            if (dock != null && hp != null)
            {
                yield return FlyTo(hp.BerthPosition, hp.dockRange * 0.8f, 60f);
                cc.throttle = 0f;
                yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
                dock.Dock();
                float until = Time.time + 8f;
                while (dock.Current != Docking.State.Docked && Time.time < until) yield return null;
                yield return new WaitForSeconds(0.5f);
                Shot("h7_docked");
                bool docked = cap.History.Skip(seen).Any(h => h.mood == Mood.Smile && h.reason.StartsWith("DOCKED"));
                Log($"  docking: {dock.Current}; {Moods(cap, seen)}");
                results.Add(("pleased to dock", docked));
                dock.Undock();
                yield return new WaitForSeconds(3f);
            }

            // a landing: intent on the way down, pleased at touchdown
            var landing = PlanetLanding.Instance;
            seen = cap.History.Count;
            if (landing != null && landing.LandOn(Point("Thalassa")))
            {
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 60f);
                yield return new WaitForSeconds(0.6f);
                Shot("h8_landed");
                bool down = cap.History.Skip(seen).Any(h => h.mood == Mood.Focused && h.reason == "GOING DOWN");
                bool touch = cap.History.Skip(seen).Any(h => h.mood == Mood.Smile && h.reason == "TOUCHDOWN");
                Log($"  landing: {landing.Current}; {Moods(cap, seen)}");
                results.Add(("intent going down, pleased at touchdown", down && touch));
                landing.TakeOff();
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);
            }
            else results.Add(("intent going down, pleased at touchdown", false));

            // a fight: contact, hits taken, guns firing, a gun destroyed
            hull.cannotDie = true;
            seen = cap.History.Count;
            int kills0 = ShipConsole.Kills;
            yield return WarpTo(kb, "Corsair Haven", "h9_haven");
            bool fought = false;
            yield return GridTest(kb, "Corsair Haven", "Haven Gun A", "h9_haven", 25f, ok => fought = ok);
            var fight = cap.History.Skip(seen).ToList();
            bool contact = fight.Any(h => h.reason == "CONTACT" || h.reason.Contains("HOSTILE"));
            bool hit = fight.Any(h => h.mood == Mood.Alarmed && h.reason.Contains("SHIELD") || h.mood == Mood.Pain || h.reason == "RETURNING FIRE");
            bool firing = fight.Any(h => h.mood == Mood.Determined);
            bool kill = ShipConsole.Kills > kills0 && fight.Any(h => h.reason == "TARGET DESTROYED");
            Log($"  the fight: {Moods(cap, seen)}; kills {ShipConsole.Kills - kills0}");
            results.Add(("in a fight: contact, under fire, firing back, a kill", contact && hit && firing && kill));
            hull.cannotDie = false;
            hull.Repair();
            if (shield != null) shield.SetOffline(false);

            // the ship lost, and back
            seen = cap.History.Count;
            hull.Damage(hull.Current + 10f);
            yield return Expect(cap, Mood.Sad, "LOST", 2f, "h10_lost", results, "grieving when the ship is lost");
            string lostAlert = ShipConsole.Alerts;
            float back = Time.time + 30f;
            while (cc.Destroyed && Time.time < back) yield return null;
            yield return Expect(cap, Mood.Determined, "BACK", 3f, "h11_back", results, "back in action after the respawn");
            Log($"  lost: alert '{lostAlert}'");
            yield return Settle(cap);

            // a message: she says it
            int talks = 0;
            CarrierDemoInput.Toast("Captain on the bridge");
            float stop = Time.time + 1f;
            while (Time.time < stop) { if (cap.Shown == Mood.Talk) talks++; yield return null; }
            // and blinks, now and then
            int blinks = 0;
            stop = Time.time + 8f;
            while (Time.time < stop) { if (cap.Shown == Mood.Blink) blinks++; yield return null; }
            Log($"  a message: {talks} frames talking in 1 s; {blinks} frames blinking in 8 s");
            results.Add(("talks when a message shows, and blinks", talks > 0 && blinks > 0));

            // ---- choosing a captain in the pause menu, by clicking her portrait
            var menu = FindAnyObjectByType<GameMenu>();
            yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
            yield return new WaitForSecondsRealtime(0.4f);
            int pick = (chosen0 + 2) % Captain.Roster.Length;
            Vector2 at = menu != null ? menu.CaptainScreenPos(pick) : Vector2.zero;
            yield return Click(mouse, new Vector2(at.x, Screen.height - at.y));
            yield return new WaitForSecondsRealtime(0.3f);
            Shot("h12_menu");
            bool clicked = Captain.Index == pick;
            int saved = PlayerPrefs.GetInt("captain", -1);
            // and with the keys: right arrow on the Captain row
            int rows = 0;
            while (menu != null && menu.SelectedLabel.StartsWith("Captain") == false && rows++ < 8) yield return Key(kb, UnityEngine.InputSystem.Key.DownArrow);
            yield return Key(kb, UnityEngine.InputSystem.Key.RightArrow);
            bool keyed = Captain.Index == (pick + 1) % Captain.Roster.Length;
            Log($"  menu: clicked {Captain.Roster[pick].name}: chosen {clicked}, saved {saved == pick}; right arrow on '{menu?.SelectedLabel}': {Captain.Current.name} ({keyed})");
            yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
            yield return new WaitForSeconds(0.3f);
            bool hello = cap.Reason == "REPORTING FOR DUTY";
            results.Add(("a captain chosen in the menu (click and keys), and saved", clicked && saved == pick && keyed && hello));

            // every captain on the console
            for (int i = 0; i < Captain.Roster.Length; i++)
            {
                Captain.Index = i;
                yield return new WaitForSeconds(2.2f);
                Shot($"h13_captain_{i + 1}_{Captain.Roster[i].id}");
            }
            Captain.Index = chosen0;     // the player's own choice back
            yield return new WaitForSeconds(0.5f);

            int passed = results.Count(x => x.Item2);
            foreach (var (name, ok) in results) Log($"  {(ok ? "ok  " : "FAIL")} {name}");
            Log(passed == results.Count
                ? $"PASS: HUD and captain ({passed} of {results.Count})"
                : $"FAIL: HUD and captain ({passed} of {results.Count} passed: failed {string.Join(", ", results.Where(x => !x.Item2).Select(x => x.Item1))})");
        }

        static string Moods(CaptainPortrait cap, int from) =>
            string.Join(" > ", cap.History.Skip(from).Select(h => $"{h.mood} '{h.reason}'"));

        /// <summary>Wait for a reaction to pass (or a few seconds).</summary>
        IEnumerator Settle(CaptainPortrait cap)
        {
            yield return new WaitForSeconds(3.5f);
        }

        /// <summary>Wait up to `seconds` for her to show `mood` with a reason containing `why`.</summary>
        IEnumerator Expect(CaptainPortrait cap, Mood mood, string why, float seconds, string shot, List<(string, bool)> results, string name)
        {
            float until = Time.time + seconds;
            while (Time.time < until && !(cap.Mood == mood && cap.Reason.Contains(why))) yield return null;
            bool ok = cap.Mood == mood && cap.Reason.Contains(why);
            yield return new WaitForSeconds(0.15f);
            Shot(shot);
            Log($"  {name}: {cap.Mood} '{cap.Reason}' (want {mood}); alerts '{ShipConsole.Alerts}'");
            results.Add((name, ok));
        }
    }
}
