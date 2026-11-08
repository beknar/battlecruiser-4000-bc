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
    /// in formation with the frigate past Jovara, and fire a volley ahead; then the game leaves
    /// and carries on alone. Without -reelcoop the scene is left out.
    /// </summary>
    public partial class DemoReel
    {
        static readonly string[] Wingmen = { "Kestrel 2", "Kestrel 3" };

        static string ReelCoopServer()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-reelcoop");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

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
            Caption("CO-OP", "Up to four players fly together through a small server for Windows or Linux, from Windows or the web", 6f);
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
            CoopClient.Disconnect();
            yield return Wait(0.5f);
            Clear();
            cc.throttle = 0f;
        }
    }
}
