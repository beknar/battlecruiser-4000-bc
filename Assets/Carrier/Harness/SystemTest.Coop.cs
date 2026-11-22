using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -cooptest (with -connect &lt;address:port&gt;; ?cooptest&amp;server=...&amp;connect on the web):
    /// co-op against a scripted second player (Tools/CoopTest/bot.py) on a real server. First
    /// this game is a guest (the bot got there first and hosts): its enemies are puppets, the
    /// orbits run on the room's clock, the bot's ship shows up, an enemy goes where the bot says,
    /// the bot's hits and shots arrive, and this game's hits on the puppet go to the bot. Then
    /// the bot leaves and this game takes the enemies over; the bot comes back as a guest and gets
    /// the enemies, its hits land on the real ones, and at Corsair Haven the guns go for the bot's
    /// ship and the hits reach the bot. With the two of them, the enemies are tougher and hit
    /// harder (CoopScaling). Last, leaving: back to a game of one, the enemies back to normal and
    /// the orbits carrying on.
    /// </summary>
    public partial class SystemTest
    {
        bool coopOnly;
        readonly List<string> botNotes = new List<string>();

        void OnBotMessage(CoopMsg m)
        {
            if (m.t == CoopWire.Test && !string.IsNullOrEmpty(m.note)) { botNotes.Add(m.note); Log($"  bot: {m.note}"); }
        }

        void Note(string note, string[] ids = null) => CoopClient.Instance?.Send(new TestMsg { note = note, ids = ids });

        /// <summary>Wait for the bot to say something starting with `prefix`; its value (after '=').</summary>
        IEnumerator BotSays(string prefix, float seconds, Action<string> got)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                var n = botNotes.LastOrDefault(x => x.StartsWith(prefix));
                if (n != null) { botNotes.Remove(n); got(n.Contains('=') ? n.Substring(n.IndexOf('=') + 1) : ""); yield break; }
                yield return null;
            }
            got(null);
        }

        static string V(Vector3 v) => $"{v.x:F1},{v.y:F1},{v.z:F1}";
        static string V(CoopWorld.NetPos v) => $"{v.p.x:F1},{v.p.y:F1},{v.p.z:F1}";

        IEnumerator CoopTest(Keyboard kb, bool quick)
        {
            var results = new List<(string, bool)>();
            CoopClient.Message += OnBotMessage;
            var world = CoopWorld.Instance;
            float until = Time.realtimeSinceStartup + 25f;
            while (!CoopClient.Active && Time.realtimeSinceStartup < until) yield return null;
            var client = CoopClient.Instance;
            if (!CoopClient.Active || world == null)
            {
                Log($"FAIL: co-op: not connected ({client?.Status ?? "no connection"})");
                yield break;
            }
            Log($"connected to {client.Url} as #{client.MyId}: room '{client.RoomLevel}', host #{client.HostId}, {client.Players.Count} players, {world.Entities} enemies");
            yield return new WaitForSeconds(2f);

            // ---- a guest
            bool guest = !client.IsHost && world.Puppeting;
            double orbit = OrbitalSystem.Instance != null ? OrbitalSystem.Instance.Time : -1, room = client.OrbitClock;
            // the orbits read the clock once a frame, so they can be a frame behind (a slow one, in software rendering)
            double frame = Time.unscaledDeltaTime;
            Log($"  a guest: host #{client.HostId}, puppets {world.Puppeting}; orbit clock {orbit:F2} s, the room's {room:F2} s (frame {frame:F2} s)");
            results.Add(("a guest: the enemies are the host's (puppets here)", guest));
            results.Add(("the orbits run on the room's clock", OrbitalSystem.Instance == null || Math.Abs(orbit - room) < 0.25 + frame));

            // an enemy for the bot to move about: the nearest with a hull
            var hull = cc.GetComponent<Health>();
            var shield = cc.GetComponentInChildren<Shield>(true);
            hull.cannotDie = true;
            string target = world.EntityIds.Where(id => world.EntityTransform(id) != null && world.EntityTransform(id).GetComponent<Health>() != null)
                .OrderBy(id => Vector3.Distance(world.EntityTransform(id).position, cc.transform.position)).FirstOrDefault();
            var tt = target != null ? world.EntityTransform(target) : null;
            var th = tt != null ? tt.GetComponent<Health>() : null;
            Vector3 front = cc.transform.position + cc.transform.forward * 1500f + Vector3.up * 50f;
            Vector3 beside = cc.transform.position + cc.transform.right * 600f;
            var gun = cc.mainTurrets.FirstOrDefault(g => g != null && g.projectilePrefab != null);
            string pf = gun != null ? gun.projectilePrefab.name : "";
            float shield0 = shield != null ? shield.Strength : 0f, hull0 = hull.Current;
            int taken0 = world.HitsTaken, shown0 = world.ShotsShown;
            Note($"ready|target={target}|front={V(world.ToNet(front))}|beside={V(world.ToNet(beside))}|rf={world.ToNet(front).rf}|pf={pf}");
            Log($"  asked the bot (the host) to fly beside me, move '{target}' 1.5 km ahead, hit me and shoot ('{pf}')");
            until = Time.realtimeSinceStartup + 10f;
            RemotePlayer bot = null;
            while (Time.realtimeSinceStartup < until)
            {
                bot = client.Players.Where(p => p.id != client.MyId).Select(p => world.RemoteOf(p.id)).FirstOrDefault(r => r != null);
                bool there = bot != null && !bot.Hidden && Vector3.Distance(bot.transform.position, beside) < 150f;
                bool moved = tt != null && Vector3.Distance(tt.position, front) < 150f;
                if (there && moved && world.HitsTaken - taken0 >= 3 && world.ShotsShown > shown0) break;
                yield return null;
            }
            float botOff = bot != null ? Vector3.Distance(bot.transform.position, beside) : -1f;
            float entOff = tt != null ? Vector3.Distance(tt.position, front) : -1f;
            float hullFrac = th != null ? th.Current / th.maxHealth : -1f;
            float lost = (shield0 - (shield != null ? shield.Strength : 0f)) + (hull0 - hull.Current);
            Log($"  the bot's ship: {(bot != null ? $"'{bot.name}', shown {!bot.Hidden}, {botOff:F0} m from where it should be, {bot.Updates} updates" : "none")}");
            Log($"  the puppet: {entOff:F0} m from where the host put it, hull {hullFrac:P0} (the host says 60 %)");
            Log($"  hits from the host: {world.HitsTaken - taken0} (shield and hull down {lost:F0}); shots shown {world.ShotsShown - shown0}");
            Shot("c1_guest");
            results.Add(("another player's ship shows, where they are", bot != null && !bot.Hidden && botOff < 150f));
            results.Add(("a puppet goes where the host says, as damaged as the host says", entOff >= 0f && entOff < 150f && Mathf.Abs(hullFrac - 0.6f) < 0.05f));
            results.Add(("the host's hits on me land, and its shots show", world.HitsTaken - taken0 >= 3 && lost > 0f && world.ShotsShown > shown0));

            // the system map: the other player's ship beside mine (its arrow under my own), chosen
            // by clicking its name; it is too close to warp to. Navigation's menu lists it as an ally.
            var smap = FindAnyObjectByType<SystemMap>();
            bool picked = false, honest = false, listed = false;
            if (smap != null && bot != null)
            {
                smap.Open();
                yield return null;
                yield return null;
                var lp = smap.LabelPosOf(bot.transform);
                picked = lp.HasValue && smap.AllyAt(lp.Value) == bot.transform;
                honest = smap.Allies.Any(a => a.ship == bot.transform && a.noWarp != null && a.noWarp.StartsWith("already with you"));
                smap.Close();
                var nav = FindAnyObjectByType<NavigationMode>();
                var rows = new System.Collections.Generic.List<MenuRow>();
                if (nav != null) nav.Rows(rows);
                listed = rows.Any(r => (object)r.tag == bot.transform);
            }
            Log($"  the map: its name clicked picks it {picked}; too close to warp to, said so {honest}; navigation's menu lists it {listed}");
            results.Add(("on the map, another player's ship beside mine is picked by its name, and listed in navigation's menu", picked && honest && listed));

            // two players here: the enemies are tougher
            float tough2 = CoopScaling.Toughness(2), built = th != null ? th.BuiltMaxHealth : 0f;
            Log($"  scaled for {world.ScaledFor} players: '{target}' hull {(th != null ? th.maxHealth : 0f):F0} (built {built:F0}, x{tough2:0.##})");
            results.Add(("with two players the enemies are tougher", world.ScaledFor == 2 && th != null && built > 0f && Mathf.Abs(th.maxHealth - built * tough2) < 1f));

            // my hits on the puppet go to the host
            int sent0 = world.HitsSentToHost;
            if (tt != null)
            {
                demo.SelectTarget(tt);
                Vector3 to = tt.position - cc.transform.position;
                cc.SetAttitude(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
                foreach (var g in cc.mainTurrets) if (g) g.autoFire = true;
                demo.ChargeMainGun();
            }
            until = Time.realtimeSinceStartup + 20f;
            while (world.HitsSentToHost - sent0 < 3 && Time.realtimeSinceStartup < until) yield return null;
            Note("de?");
            string de = null;
            yield return BotSays("de=", 6f, v => de = v);
            int.TryParse(de ?? "-1", out int deN);
            Log($"  my hits on the puppet: {world.HitsSentToHost - sent0} sent to the host; the bot got {deN}; the puppet's hull here {(th != null ? th.Current / th.maxHealth : -1f):P0} (unchanged: the host's game decides)");
            results.Add(("my hits on a puppet go to the host", world.HitsSentToHost - sent0 >= 3 && deN >= 3 && th != null && Mathf.Abs(th.Current / th.maxHealth - 0.6f) < 0.05f));
            foreach (var g in cc.mainTurrets) if (g) g.autoFire = false;
            cc.SetTarget(null);

            // the host makes an enemy during play (a launched drone): it is made here too, from
            // its key, a puppet where the host has it; then it goes, and goes here
            {
                const string rtId = "rt:99:1";
                Vector3 at = cc.transform.position + cc.transform.forward * 900f + Vector3.up * 120f;
                var np = world.ToNet(at);
                Note($"spawn|id={rtId}|key=EnemyDrone|at={V(np)}|rf={np.rf}");
                until = Time.realtimeSinceStartup + 8f;
                while (world.EntityTransform(rtId) == null && Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForSeconds(0.5f);
                var made = world.EntityTransform(rtId);
                float madeOff = made != null ? Vector3.Distance(made.position, at) : -1f;
                bool puppet = made != null && made.GetComponent<NetPuppet>() != null && made.GetComponent<TargetInfo>() != null;
                Note("despawn");
                until = Time.realtimeSinceStartup + 8f;
                while (made != null && Time.realtimeSinceStartup < until) yield return null;
                Log($"  the host made an enemy during play: here {(madeOff >= 0f ? $"{madeOff:F0} m from where the host has it, a puppet {puppet}" : "never")}; gone when the host said: {made == null && world.EntityTransform(rtId) == null}");
                results.Add(("an enemy the host makes during play is made here, a puppet where the host has it, and goes when it goes",
                             madeOff >= 0f && madeOff < 150f && puppet && made == null && world.EntityTransform(rtId) == null));
            }

            // ---- the host leaves: this game takes over
            Note("leave");
            until = Time.realtimeSinceStartup + 10f;
            while (!client.IsHost && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(0.5f);
            Log($"  the bot left: host now #{client.HostId} (me #{client.MyId}); puppets {world.Puppeting}");
            results.Add(("the host leaves: this game takes the enemies over", client.IsHost && !world.Puppeting));

            // the bot comes back, a guest now
            until = Time.realtimeSinceStartup + 15f;
            while (client.Players.Count < 2 && Time.realtimeSinceStartup < until) yield return null;
            string es = null;
            yield return BotSays("es=", 10f, v => es = v);
            int.TryParse(es ?? "-1", out int esN);
            // (the hull and any shield: a Vrask ship's shield takes the first hits)
            var tsh = th != null ? th.GetComponentInChildren<Shield>() : null;
            float Toughness() => (th != null ? th.Current : 0f) + (tsh != null && tsh.Up ? tsh.Strength : 0f);
            float before = Toughness();
            int applied0 = world.HitsAppliedForOthers;
            Note($"hit|target={target}");
            until = Time.realtimeSinceStartup + 8f;
            while (world.HitsAppliedForOthers - applied0 < 3 && Time.realtimeSinceStartup < until) yield return null;
            float after = Toughness();
            Log($"  the bot back as a guest: it got {esN} enemies from this game; its hits applied {world.HitsAppliedForOthers - applied0} ('{target}' hull and shield {before:F0} -> {after:F0})");
            results.Add(("as host, the enemies go to the guests", esN == world.Entities));
            results.Add(("a guest's hits land on the real enemy", world.HitsAppliedForOthers - applied0 >= 3 && after < before));

            // as host, an enemy made during play: the guest gets it with its key; the bot's game
            // says nothing of what it has (an older game), so the drone's hits on it are dropped
            {
                var prefab = CoopSpawnables.Find("EnemyDrone");
                GameObject drone = null;
                int rtN = -1, goneN = -1, dropped0 = world.HitsDroppedForOlder, sentP0 = world.HitsSentToPlayers;
                if (prefab != null)
                {
                    drone = Instantiate(prefab, cc.transform.position + cc.transform.right * -900f + Vector3.up * 150f, Quaternion.identity);
                    drone.name = "Co-op test drone";
                    foreach (var b in drone.GetComponentsInChildren<MonoBehaviour>()) if (b is CombatPilot || b is EnemyWeapons) b.enabled = false;
                    CoopSpawn.Spawned(drone, "EnemyDrone");
                    yield return new WaitForSeconds(1.5f);
                    Note("rt?");
                    string rt = null;
                    yield return BotSays("rt=", 6f, v => rt = v);
                    int.TryParse(rt ?? "-1", out rtN);
                    // a hit from the drone on the bot's ship (the bot back as a guest: its ship now)
                    var botNow = client.Players.Where(p => p.id != client.MyId).Select(p => world.RemoteOf(p.id)).FirstOrDefault(r => r != null);
                    var botHealth = botNow != null ? botNow.GetComponent<Health>() : null;
                    if (botHealth != null)
                    {
                        Health.Source = drone.transform;
                        botHealth.Damage(50f, botHealth.transform.position, HitKind.Projectile);
                        Health.Source = null;
                    }
                    CoopSpawn.Gone(drone);
                    Destroy(drone);
                    yield return new WaitForSeconds(1f);
                    Note("rtgone?");
                    string gone = null;
                    yield return BotSays("rtgone=", 6f, v => gone = v);
                    int.TryParse(gone ?? "-1", out goneN);
                }
                Log($"  an enemy made here during play: the guest got {rtN} with a key, and was told it's gone ({goneN}); " +
                    $"its hit on the guest's older game dropped {world.HitsDroppedForOlder - dropped0}, sent {world.HitsSentToPlayers - sentP0}; enemies here {world.Entities} (made during play {world.RuntimeEntities})");
                results.Add(("as host, an enemy made during play goes to the guests with its key, and its going too", prefab != null && rtN == 1 && goneN == 1 && world.RuntimeEntities == 0));
                results.Add(("an older guest's game isn't hit by what it hasn't got", world.HitsDroppedForOlder - dropped0 == 1 && world.HitsSentToPlayers == sentP0));
                Log($"  the enemies sent: {world.EntityUpdates} updates, {world.EntityBytes / Mathf.Max(1, world.EntityUpdates)} bytes each on average, {world.LastSent} in the last");
            }

            // ---- at Corsair Haven the guns go for the guest's ship
            if (!quick)
            {
                if (th != null) th.Repair();
                // the bot's hits just now: wait until the drive will warp again
                until = Time.realtimeSinceStartup + 20f;
                while (!drive.CanWarp(out _) && Time.realtimeSinceStartup < until) yield return null;
                yield return WarpTo(kb, "Corsair Haven", "c2_haven");
                var grid = DefenseGrid.All.FirstOrDefault(g => g.gridName == "Corsair Haven");
                var haven = grid != null ? grid.weapons.FirstOrDefault(w => w != null) : null;
                int toPlayers0 = world.HitsSentToPlayers;
                if (haven != null)
                {
                    Vector3 o = (cc.transform.position - haven.transform.position);
                    o.y = 0f;
                    Vector3 spot = haven.transform.position + o.normalized * 3500f + Vector3.up * 200f;
                    Note($"goto={V(world.ToNet(spot))}|rf={world.ToNet(spot).rf}");
                    until = Time.realtimeSinceStartup + 45f;
                    while (world.HitsSentToPlayers - toPlayers0 < 3 && Time.realtimeSinceStartup < until) yield return null;
                }
                Shot("c3_haven");
                Note("dp?");
                string dp = null;
                yield return BotSays("dp=", 6f, v => dp = v);
                int.TryParse(dp ?? "-1", out int dpN);
                Log($"  at Corsair Haven: grid engaged {grid != null && grid.Engaged}; hits on the bot's ship sent {world.HitsSentToPlayers - toPlayers0}, the bot got {dpN}");
                results.Add(("the host's enemies go for a guest's ship, and the hits reach them", world.HitsSentToPlayers - toPlayers0 >= 3 && dpN >= 3));
            }

            // an enemy's shot, fired here (the host), is as hard as two players call for
            EnemyWeapons gunner = null;
            foreach (var id in world.EntityIds)
            {
                var et = world.EntityTransform(id);
                var w = et != null ? et.GetComponentInChildren<EnemyWeapons>() : null;
                if (w != null && w.gunPrefab != null) { gunner = w; break; }
            }
            float fire2 = CoopScaling.Firepower(2), built2 = gunner != null ? gunner.gunPrefab.damage : 0f, fired = -1f;
            if (gunner != null)
            {
                // straight up, well clear of everything, then gone
                var shot = Instantiate(gunner.gunPrefab, gunner.transform.position + Vector3.up * 20000f, Quaternion.LookRotation(Vector3.up));
                shot.owner = gunner.transform;
                yield return null;
                yield return null;
                if (shot != null) { fired = shot.damage; Destroy(shot.gameObject); }
            }
            Log($"  a shot from '{(gunner != null ? gunner.name : "none")}': damage {fired:F1} (built {built2:F1}, x{fire2:0.##} for two players)");
            results.Add(("with two players the enemies' shots hit harder", gunner != null && built2 > 0f && Mathf.Abs(fired - built2 * fire2) < 0.01f));

            // ---- leaving
            Note("bye");
            yield return new WaitForSeconds(0.5f);
            double t0 = OrbitalSystem.Instance != null ? OrbitalSystem.Instance.Time : 0;
            CoopClient.Disconnect();
            yield return new WaitForSeconds(1f);
            double t1 = OrbitalSystem.Instance != null ? OrbitalSystem.Instance.Time : 0;
            bool alone = !CoopClient.Active && !world.Puppeting && OrbitalSystem.ClockOverride == null && world.Remotes == 0;
            Log($"  left: connected {CoopClient.Active}, puppets {world.Puppeting}, other ships {world.Remotes}; the orbit clock went {t0:F1} -> {t1:F1} s");
            results.Add(("leaving: back to a game of one, the orbits carrying on", alone && (OrbitalSystem.Instance == null || Math.Abs(t1 - t0 - 1.0) < 0.5)));
            Log($"  alone: scaled for {world.ScaledFor}; '{target}' hull {(th != null ? th.maxHealth : 0f):F0} (built {built:F0})");
            results.Add(("alone again, the enemies are back to normal", world.ScaledFor == 1 && th != null && Mathf.Abs(th.maxHealth - built) < 1f));
            hull.cannotDie = false;
            CoopClient.Message -= OnBotMessage;

            int passed = results.Count(x => x.Item2);
            foreach (var (name, ok) in results) Log($"  {(ok ? "ok  " : "FAIL")} {name}");
            Log(passed == results.Count
                ? $"PASS: co-op ({passed} of {results.Count})"
                : $"FAIL: co-op ({passed} of {results.Count} passed: failed {string.Join(", ", results.Where(x => !x.Item2).Select(x => x.Item1))})");
        }
    }
}
