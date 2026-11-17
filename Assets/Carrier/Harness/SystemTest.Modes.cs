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

            // ---- the music: a playlist for calm and for combat, changing track (no longer than 3
            // minutes a track: made due now), Shift+N the next song, and it can be turned off
            var music = FindAnyObjectByType<MusicDirector>();
            if (music != null && music.enabled && MusicDirector.Playing)
            {
                Check(MusicDirector.CalmCount >= 20 && MusicDirector.CombatCount >= 20,
                      $"the playlist: {MusicDirector.CalmCount} calm tracks, {MusicDirector.CombatCount} combat tracks");
                int cycles0 = MusicDirector.Cycles;
                string song0 = MusicDirector.NowPlaying;
                float fade0 = music.cycleFade;
                music.cycleFade = 0.5f;
                music.ChangeTrackSoon();
                float mu = Time.time + 5f;
                while (Time.time < mu && MusicDirector.Cycles == cycles0) yield return null;
                yield return new WaitForSeconds(1f);
                string song1 = MusicDirector.NowPlaying;
                // (the songs are picked at random: logged apart, so the check reads the same every run)
                Log($"  the music: '{song0}' to '{song1}'");
                Check(MusicDirector.Cycles > cycles0 && song1 != song0 && music.cycleMin >= 30f && music.cycleMax <= 180f,
                      $"the music changes track, crossfading (every {music.cycleMin:0}-{music.cycleMax:0} s)");
                int cycles1 = MusicDirector.Cycles;
                yield return Keys(kb, K.LeftShift, K.N);
                yield return new WaitForSeconds(1f);
                string song2 = MusicDirector.NowPlaying;
                Check(MusicDirector.Cycles > cycles1 && song2 != song1, $"Shift+N: the next song ('{song2}')");
                MusicDirector.Previous();
                yield return new WaitForSeconds(1f);
                Check(MusicDirector.NowPlaying == song1, $"and the one before again ('{MusicDirector.NowPlaying}')");
                // off: silent, the rest of the sound going on; on again
                MusicDirector.Toggle();
                yield return new WaitForSeconds(2f);
                bool silent = !MusicDirector.Playing && MusicDirector.Volume < 0.001f && !SoundFX.Muted;
                MusicDirector.Toggle();
                yield return new WaitForSeconds(2.5f);
                bool musicBack = MusicDirector.Playing && MusicDirector.Volume > 0.05f && MusicDirector.On;
                Check(silent && musicBack, $"the music turns off (silent {silent}, the sound still on) and on again ({musicBack}, '{MusicDirector.NowPlaying}')");
                music.cycleFade = fade0;
            }
            else Log("  (no music here: the music check is skipped)");

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

            // ---- U: fly to the target (9 km ahead now), and line up on it there
            var fly = AutoFly.Instance;
            dummy.transform.position = t.position + t.forward * 9000f + t.right * 1500f;
            demo.SelectTarget(dummy.transform);
            int arrivals0 = fly != null ? fly.Arrivals : 0;
            yield return Key(kb, K.U);
            bool flying = fly != null && fly.Active;
            float fu = Time.time + 90f;
            while (Time.time < fu && fly != null && fly.Active) yield return null;
            float there = Vector3.Distance(cc.transform.position, dummy.transform.position);
            Check(flying && fly.Arrivals == arrivals0 + 1 && combat.LiningUp,
                  $"U flies to the target ({there / 1000f:F1} km off on arrival, after {Time.time - (fu - 90f):F0} s) and lines up on it there (lining up {combat.LiningUp})");
            Shot("modes_flown_to_target");
            combat.StopLineUp();
            cc.throttle = 0f;

            // ---- a friendly station or installation in range can be targeted (T), to fly to; the
            // guns hold their fire on it
            {
                var friendly = Targets.Friendly.Where(f => TargetDisplay.InRange(f, cc.transform.position)).ToList();
                Transform got = null;
                for (int i = 0; i < Targets.AllWithFriendly().Count + 1 && got == null; i++)
                {
                    yield return Key(kb, K.T);
                    if (Targets.IsFriendly(cc.target)) got = cc.target;
                }
                // (none within 30 km here: the nearest, chosen as a click or the menu would)
                if (friendly.Count == 0)
                {
                    got = Targets.Friendly.OrderBy(f => (f.position - cc.transform.position).sqrMagnitude).FirstOrDefault();
                    if (got != null) demo.SelectTarget(got);
                    yield return null;
                }
                bool hold = got != null && cc.target == got && cc.mainTurrets.All(g => g == null || g.target == null);
                bool observatory = Targets.Friendly.Any(f => Targets.NameOf(f) == "Thalassa L1 Observatory");
                Check(got != null && hold && observatory,
                      $"a friendly installation can be targeted ({Targets.NameOf(got)}, {(friendly.Count > 0 ? $"by T, of {friendly.Count} in range" : "the nearest")}), " +
                      $"the guns holding their fire ({hold}); the Thalassa L1 Observatory among them ({observatory})");
                demo.ClearTarget();
            }

            Targets.Remove(dummy.transform);
            Destroy(dummy);
            cc.throttle = 0f;

            // ---- salvage: a hostile destroyed leaves a wreck; chosen in the menu, Z strips it into the hold
            yield return SalvageTest(kb, Check);

            // ---- the HUD's settings: the captain hidden, the HUD smaller
            float wide = ShipConsole.Rect.width;
            HudSettings.ShowCaptain = false;
            yield return null;
            yield return null;
            float narrow = ShipConsole.Rect.width;
            Shot("modes_no_captain");
            HudSettings.ShowCaptain = true;
            Check(narrow > 0f && narrow < wide * 0.8f, $"the captain hidden, the console is narrower ({wide:F0} -> {narrow:F0} px)");
            float size0 = Hud.UserScale;
            HudSettings.ChangeSize(-1);
            yield return null;
            yield return null;
            float smaller = ShipConsole.Rect.width;
            HudSettings.ChangeSize(+1);
            bool back = Mathf.Approximately(HudSettings.Size, size0);
            Hud.UserScale = size0;
            Check(back && smaller < wide, $"the HUD a size smaller: the console {wide:F0} -> {smaller:F0} px, and back to {HudSettings.Size * 100f:F0}%");
            // ---- the enemies' spawns, location by location
            yield return SpawnTest(Check);
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

            // a second, 7 km off: Shift+Z chooses it, Z flies there and starts the beam on arrival
            var far = new GameObject("Distant hulk");
            far.SetActive(false);
            var fbox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fbox.transform.SetParent(far.transform, false);
            fbox.transform.localScale = new Vector3(40f, 20f, 120f);
            Object.Destroy(fbox.GetComponent<Collider>());
            var fh = far.AddComponent<Health>();
            fh.maxHealth = 500f;
            far.AddComponent<TargetInfo>().displayName = "Distant hulk";
            far.transform.position = t.position + t.forward * 7000f - t.right * 1200f;
            far.SetActive(true);
            var farAt = far.transform;
            Targets.Add(farAt);
            yield return null;
            fh.Damage(1e6f);
            yield return null;
            Targets.Remove(farAt);
            var wreck2 = Wreck.All.LastOrDefault();
            yield return Keys(kb, K.LeftShift, K.Z);
            bool chose = wreck2 != null && salvage.Chosen == wreck2;
            yield return Key(kb, K.Z);
            var fly = AutoFly.Instance;
            bool flies = fly != null && fly.Active;
            float fu = Time.time + 90f;
            while (Time.time < fu && fly != null && fly.Active) yield return null;
            yield return null;
            Check(chose && flies && salvage.Working == wreck2,
                  $"Shift+Z chooses the far wreck ({chose}), Z flies there ({flies}) and starts the beam on arrival ({(salvage.Working != null ? salvage.Working.Name : "none")}, " +
                  $"{(wreck2 != null ? salvage.DistanceTo(wreck2) / 1000f : -1f):F1} km off)");
            Shot("modes_flown_to_wreck");
            if (salvage.Working != null) salvage.Stop();
            cc.throttle = 0f;
            if (far != null) Destroy(far);
        }

        /// <summary>A location's enemies all destroyed: it is quiet (no spawns) till the quiet
        /// is over, then they all come back; a spawn brings back any destroyed since.</summary>
        IEnumerator SpawnTest(System.Action<bool, string> Check)
        {
            var sd = SpawnDirector.Instance;
            if (sd == null || sd.Sites.Count == 0) { Check(false, "spawns: no locations"); yield break; }
            Log($"  spawn locations: {string.Join(", ", sd.Sites.Select(x => $"{x.name} ({x.enemies.Count})"))}; every {sd.spawnEvery / 60f:0} min, quiet {sd.quietAfterCleared / 60f:0} min once cleared");
            var site = sd.Sites.OrderBy(x => x.enemies.Count).First();
            int count = site.enemies.Count;
            // (a spawn leaves one only just destroyed, while it breaks up: no wait beyond that here)
            float minDead0 = sd.minDead;
            sd.minDead = 0f;
            foreach (var h in site.enemies) if (h != null && !h.Dead) h.Damage(1e9f);
            yield return new WaitForSeconds(3f);
            Check(site.Cleared && site.Alive == 0, $"{site.name}: all {count} destroyed, it is cleared (quiet for {sd.quietAfterCleared / 60f:0} min)");
            sd.SpawnAllNow();
            yield return null;
            Check(site.Alive == 0, $"no spawns there while it is quiet ({site.Alive} back)");
            yield return new WaitForSeconds(5f);
            sd.EndQuietNow();
            yield return new WaitForSeconds(1.5f);
            Check(!site.Cleared && site.Alive == count, $"the quiet over, they spawn again: {site.Alive} of {count}");
            site.enemies[0].Damage(1e9f);
            yield return new WaitForSeconds(1f);
            int down = count - site.Alive;
            sd.SpawnAllNow();
            yield return null;
            bool waited = site.Alive == count - 1;   // (only just destroyed: left for the next spawn)
            yield return new WaitForSeconds(8f);
            sd.SpawnAllNow();
            yield return null;
            sd.minDead = minDead0;
            Check(down == 1 && waited && site.Alive == count,
                  $"a spawn (every {sd.spawnEvery / 60f:0} min) brings back the one destroyed, once its breakup is over ({down} down, left at first {waited}, {site.Alive} of {count} after)");
        }
    }
}
