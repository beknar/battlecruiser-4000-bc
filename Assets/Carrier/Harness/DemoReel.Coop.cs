using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The co-op scene of the reel of everything: -reelcoop &lt;host:port&gt; points it at a co-op
    /// server where two scripted wingmen (Tools/CoopTest/wingman.py) are waiting. The game joins
    /// first (so it hosts, and runs the enemies as usual), the wingmen come in as guests and fly
    /// in formation with the frigate past Jovara, and fire a volley ahead. Then, with -reelbot,
    /// the AI wingman: another game run as an AI player (-coopbot, no window), started by the
    /// reel's runner once this game hosts, is ordered on the comms channel (its status, follow
    /// me, a warp away). Then the game leaves and carries on alone. Without -reelcoop the scene
    /// is left out.
    /// </summary>
    public partial class DemoReel
    {
        static readonly string[] Wingmen = { "Kestrel 2", "Kestrel 3" };

        static string ReelCoopServer() => LaunchArgs.ValueOf("-reelcoop");

        void CoopNote(string note) => CoopClient.Instance?.Send(new TestMsg { note = note });

        /// <summary>A camera offset that puts the ship up and to the right of the middle of the
        /// picture, clear of the caption (left of centre) and the console (bottom left); `back`
        /// metres behind it is the middle of what is framed (a formation's middle).</summary>
        Vector3 ClearOfCaption(float yaw, float distance, float back = 0f)
        {
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            return demo.ShipViewOffset - right * distance * 0.2f - Vector3.up * distance * 0.1f - cc.transform.forward * back;
        }

        IEnumerator CoopFlight()
        {
            string server = ReelCoopServer();
            if (server == null) { Note("no -reelcoop server: the co-op scene is left out"); yield break; }
            int colon = server.LastIndexOf(':');
            string host = colon > 0 ? server.Substring(0, colon) : server;
            int port = colon > 0 && int.TryParse(server.Substring(colon + 1), out int p) ? p : 7777;
            var jovara = WP("Jovara");
            if (jovara == null) { Note("no Jovara"); yield break; }

            // out past Jovara, the planet off to one side, cruising
            Repair();
            Vector3 from = jovara.Position + Quaternion.Euler(0f, SunBehind(jovara.Position, 0f) + 180f, 0f) * Vector3.forward * jovara.bodyRadius * 4.2f;
            Place(jovara.ArrivalPoint(from), jovara.Position);
            // across the planet's face rather than straight at it
            cc.SetAttitude(cc.Heading + 70f, 0f);
            cc.SetSpawn(cc.transform.position, cc.transform.rotation);
            cc.throttle = 0.3f;

            // on to the server; the wingmen follow us into the level
            CoopClient.Connect(host, port, "Kestrel 1");
            float until = Time.realtimeSinceStartup + 20f;
            var world = CoopWorld.Instance;
            int shown = 0;
            while (Time.realtimeSinceStartup < until)
            {
                if (CoopClient.Active && world != null)
                {
                    var client = CoopClient.Instance;
                    shown = client.Players.Where(pl => pl.id != client.MyId).Select(pl => world.RemoteOf(pl.id)).Count(r => r != null && !r.Hidden);
                    if (shown >= Wingmen.Length) break;
                }
                yield return null;
            }
            if (!CoopClient.Active) { Note($"couldn't reach the co-op server at {server}: {CoopClient.Instance?.Status}"); CoopClient.Disconnect(); yield break; }
            Note($"co-op: on {CoopClient.Instance.Url} as #{CoopClient.Instance.MyId}, host {CoopClient.Instance.IsHost}, {shown} wingmen in sight");
            // the formation: one off each wing, a little behind and above
            float d = D;
            CoopNote($"slot|{Wingmen[0]}={d * 0.75f:F0},{d * 0.08f:F0},{-d * 0.55f:F0}|{Wingmen[1]}={-d * 0.75f:F0},{d * 0.12f:F0},{-d * 0.6f:F0}");
            yield return Wait(1.5f);

            // from behind and above: three frigates, three players
            float yaw = cc.Heading - 45f;                                       // (Jovara behind them)
            Shot(490, cc.transform, ClearOfCaption(yaw, d * 2.7f, d * 0.3f), d * 2.7f, yaw, 15f, 0.6f);
            yield return FadeIn();
            Caption("CO-OP", "Up to seven players fly together through a small server for Windows or Linux, from Windows or the web", 6f);
            yield return Wait(6f);

            // a slow turn, the wingmen holding their places, and a volley ahead
            demo.autoRudder = 0.22f;
            yaw = cc.Heading - 100f;
            Shot(491, cc.transform, ClearOfCaption(yaw, d * 2.9f, d * 0.3f), d * 2.9f, yaw, 7f, -0.8f);
            var gun = cc.mainTurrets.FirstOrDefault(g => g != null && g.projectilePrefab != null);
            if (gun != null) CoopNote($"fire|pf={gun.projectilePrefab.name}|s=3.5");
            Caption("ONE FIGHT", "Ships, shots, missiles and beams are shared, and the enemies go for whoever is nearest", 5.5f);
            yield return Wait(5.5f);
            yield return FadeOut();

            Note($"co-op: {CoopWorld.Instance?.ShotsShown ?? 0} of the wingmen's shots shown");
            CoopNote("bye");
            yield return Wait(0.3f);
            demo.autoRudder = null;
            if (LaunchArgs.Has("-reelbot")) yield return AiWingman();
            CoopClient.Disconnect();
            yield return Wait(0.5f);
            Clear();
            cc.throttle = 0f;
        }

        /// <summary>The AI wingman: found wherever it came into the level and held there (off
        /// camera), the frigate brought up behind it; then ordered on the comms channel: its
        /// status, follow me (it forms up on the wing), and a warp away.</summary>
        IEnumerator AiWingman()
        {
            var client = CoopClient.Instance;
            var world = CoopWorld.Instance;
            if (client == null || world == null) yield break;
            Note("co-op: waiting for the AI wingman");
            CoopPlayer bot = null;
            RemotePlayer ship = null;
            bool held = false;
            float until = Time.realtimeSinceStartup + 300f;
            while (Time.realtimeSinceStartup < until && CoopClient.Active)
            {
                bot = client.Players.FirstOrDefault(p => p != null && p.id != client.MyId && p.client == CoopBot.ClientName);
                // (after its hello on the channel: before that it isn't flying yet, and its own
                // first order to itself, to form up on a player, would follow the hold)
                if (bot != null && !held && CoopChat.Lines.Any(l => l.from == bot.id))
                {
                    yield return new WaitForSecondsRealtime(1f);
                    held = CoopChat.Say($"{bot.name}, hold");
                    Note($"co-op: {bot.name} is here (#{bot.id}); told to hold");
                    yield return new WaitForSecondsRealtime(5f);       // (a warp it had started charging goes ahead)
                }
                ship = bot != null ? world.RemoteOf(bot.id) : null;
                if (held && ship != null && !ship.Hidden)
                {
                    // still where it is (not in a warp) over a couple of seconds
                    Vector3 p0 = ship.transform.position;
                    yield return new WaitForSecondsRealtime(2f);
                    if (ship != null && !ship.Hidden && Vector3.Distance(p0, ship.transform.position) < 600f) break;
                }
                yield return null;
            }
            if (bot == null || ship == null || ship.Hidden || !held) { Note($"co-op: no AI wingman to show (here {bot != null}, held {held})"); yield break; }
            string name = bot.name;

            // in behind it and off to one side, facing the same way
            Vector3 bp = ship.transform.position, f = ship.transform.forward;
            f.y = 0f;
            f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Place(bp - f * 750f + right * 260f + Vector3.up * 60f, bp + f * 3000f + right * 260f);
            yield return Wait(1.5f);
            if (ship == null) { Note($"co-op: {name} left"); yield break; }
            Note($"co-op: {name} {Vector3.Distance(cc.transform.position, ship.transform.position):F0} m ahead, near {WarpPoint.Nearest(cc.transform.position)?.displayName}");
            LookPast(495, ship.transform.position, D * 1.5f, 14f, 8f, 0.5f);
            cam.offset = ClearOfCaption(cam.yaw, D * 1.5f);
            yield return FadeIn();
            Caption("AI WINGMAN", $"{name} is an AI player: another copy of the game, flying its own ship and taking orders on the comms channel", 6f);
            yield return Wait(1f);
            CoopChat.Say($"{name}, status");
            yield return Wait(4f);
            CoopChat.Say($"{name}, follow me");
            cc.throttle = 0.3f;
            yield return Wait(1.5f);

            // the two ships: it turning in and closing up on the wing
            float yaw = cc.Heading - 130f;
            Shot(496, cc.transform, ClearOfCaption(yaw, D * 2.2f), D * 2.2f, yaw, 12f, 1f);
            Caption("MCP SERVER", "Its HTTP API, wrapped as an MCP server, lets Claude or any AI client fly it and talk to the players", 6f);
            yield return Wait(7f);
            if (ship == null) { Note($"co-op: {name} left"); yield return FadeOut(); yield break; }
            Note($"co-op: {name} {Vector3.Distance(cc.transform.position, ship.transform.position):F0} m off after 'follow me'");

            // and away: a warp to the farther of the Highport and Jovara
            var a = WP("Thalassa Highport");
            var b = WP("Jovara");
            var dest = a == null ? b : b == null ? a
                : Vector3.Distance(ship.transform.position, a.Position) > Vector3.Distance(ship.transform.position, b.Position) ? a : b;
            if (dest != null)
            {
                CoopChat.Say($"{name}, warp to {dest.displayName}");
                Shot(497, ship.transform, Vector3.zero, 700f, Bearing(cc.transform.position, ship.transform.position) + 120f, 10f, 2f);
                Caption("ORDERS", "Follow, hold, attack, warp to a place, dock, regroup, status: from a player in chat, or from an AI through the API", 6f);
                yield return Wait(6f);
            }
            yield return FadeOut();
            int said = CoopChat.Lines.Count(l => l.from == bot.id);
            Note($"co-op: {name} said {said} lines: {string.Join(" | ", CoopChat.Lines.Where(l => l.from == bot.id).Select(l => l.text))}");
            cc.throttle = 0f;
        }
    }
}
