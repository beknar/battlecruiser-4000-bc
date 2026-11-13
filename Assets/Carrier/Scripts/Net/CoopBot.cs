using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// An AI player in a co-op game (-coopbot, with -connect host:port -name Name): the game runs
    /// as one more player, its ship flown by a Wingman pilot, and it talks on the comms channel
    /// (CoopChat). It can run without a window (-batchmode -nographics). It flies the starting
    /// frigate unless given one (-frigate), and never touches the player's save.
    ///
    /// Players order it on the comms channel, by its name: "Wingman, follow me", "@Wingman attack
    /// the cruiser", "Wingman hold", "Wingman warp to Thalassa Highport", "Wingman dock",
    /// "Wingman status" ("all ships", "everyone" or "wingmen" orders every bot). It answers, and
    /// says when it starts a fight, finishes one, arrives or can't do what it was told.
    ///
    /// It also answers on a small HTTP API on this machine only (127.0.0.1, -botport, 7781 by
    /// default), for an AI to fly it (Tools/CoopAI/coop_ai_mcp.py makes it an MCP server):
    ///   GET  /status            its ship, its order, what is near it, the players
    ///   GET  /messages?after=N  the comms channel after message N
    ///   GET  /destinations      the places to warp to, nearest first
    ///   POST /say   {text}      say something on the comms channel
    ///   POST /order {order, target}  follow|hold|attack|goto|regroup|dock
    ///   POST /quit              close the game
    /// Requests are read on a thread of their own and answered on the main thread, a frame later.
    /// </summary>
    public class CoopBot : MonoBehaviour
    {
        public const int DefaultPort = 7781;
        /// <summary>What an AI player calls itself in its hello (the players list shows it).</summary>
        public const string ClientName = "bot-ai";

        public static CoopBot Instance { get; private set; }
        public static bool Enabled => LaunchArgs.Has("-coopbot");

        public Wingman Pilot { get; private set; }
        public int Port { get; private set; }
        /// <summary>Orders carried out (from the comms and the API).</summary>
        public int Orders { get; private set; }

        HttpListener http;
        Thread listener;
        readonly ConcurrentQueue<(HttpListenerContext ctx, string body)> requests = new ConcurrentQueue<(HttpListenerContext, string)>();
        int lastSpeaker;
        // (the leader, kept when the level restarts and the pilot with it)
        int lastLeader;
        bool greeted;
        string lastDoing = "";
        bool wasFighting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Enabled || Instance != null) return;
            var go = new GameObject("Coop bot");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CoopBot>();
        }

        void Start()
        {
            Application.runInBackground = true;
            if (Application.isBatchMode) Application.targetFrameRate = 30;
            Port = int.TryParse(LaunchArgs.ValueOf("-botport"), out int p) && p > 0 && p < 65536 ? p : DefaultPort;
            CoopChat.Received += Heard;
            CoopClient.Message += OnMessage;
            StartApi();
        }

        void OnDestroy()
        {
            CoopChat.Received -= Heard;
            CoopClient.Message -= OnMessage;
            StopApi();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => StopApi();

        string Me => CoopClient.Instance != null && CoopClient.Instance.MyName.Length > 0 ? CoopClient.Instance.MyName : "Wingman";

        static bool IsBot(CoopPlayer p) => p.client == ClientName || p.client == "bot";

        void Update()
        {
            // the pilot, on the player's ship in whichever level this is
            if (Pilot == null)
            {
                var demo = FindAnyObjectByType<CarrierDemoInput>();
                if (demo != null && demo.carrier != null)
                {
                    Pilot = demo.GetComponent<Wingman>();
                    if (Pilot == null) Pilot = demo.gameObject.AddComponent<Wingman>();
                    Pilot.demo = demo;
                    if (lastLeader != 0) Pilot.Follow(lastLeader);
                }
            }
            if (!greeted && CoopClient.Active && Pilot != null)
            {
                greeted = true;
                var human = Humans().FirstOrDefault();
                if (human != null) { Pilot.Follow(human.id); lastLeader = lastSpeaker = human.id; }
                CoopChat.Say($"{Me} on station{(human != null ? $", forming up on {human.name}" : "")}. Orders by name: \"{Me}, follow me / hold / attack / warp to <place> / dock / status\".");
            }
            if (!CoopClient.Active) greeted = false;
            // says what it starts doing, when it's worth saying
            if (Pilot != null && CoopClient.Active && Pilot.Doing != lastDoing)
            {
                string now = Pilot.Doing;
                bool worth = now.StartsWith("target destroyed") || now.StartsWith("arrived") || now.StartsWith("can't");
                lastDoing = now;
                if (worth) CoopChat.Say($"{Me}: {now}.");
            }
            // a fight starting is said once, whatever it says it is doing on the way in
            bool fighting = Pilot != null && Pilot.Fighting;
            if (fighting && !wasFighting && CoopClient.Active)
            {
                var info = Pilot.Target.GetComponent<TargetInfo>();
                CoopChat.Say($"{Me}: engaging {(info != null && !string.IsNullOrEmpty(info.Name) ? info.Name : Pilot.Target.name)}.");
            }
            wasFighting = fighting;
            while (requests.TryDequeue(out var r)) Serve(r.ctx, r.body);
        }

        IEnumerable<CoopPlayer> Humans()
        {
            var c = CoopClient.Instance;
            if (c == null) return Enumerable.Empty<CoopPlayer>();
            return c.Players.Where(p => p != null && p.id != c.MyId && !IsBot(p));
        }

        /// <summary>A player joined: with nobody to follow yet, it forms up on them.</summary>
        void OnMessage(CoopMsg m)
        {
            if (m.t != CoopWire.Join || m.p == null || IsBot(m.p) || Pilot == null) return;
            if (Pilot.LeaderId != 0 || Pilot.Current != Wingman.Order.Hold || Pilot.Released) return;
            Pilot.Follow(m.p.id);
            lastLeader = m.p.id;
            CoopChat.Say($"{Me}: welcome, {m.p.name}. Forming up on you; orders by name (\"{Me}, help\").");
        }

        // ------------------------------------------------------------ orders on the comms channel

        void Heard(CoopChat.Line line)
        {
            var c = CoopClient.Instance;
            if (c == null || line.from == c.MyId) return;
            lastSpeaker = line.from;
            string rest = AddressedPart(line.text, Me);
            if (rest == null) return;
            string reply = Command(rest, line.from);
            if (!string.IsNullOrEmpty(reply)) CoopChat.Say($"{Me}: {reply}");
        }

        /// <summary>What follows the bot's name (or "@name", "all ships", "everyone", "wingmen") at
        /// the start of a message; null if the message isn't to it.</summary>
        public static string AddressedPart(string text, string me)
        {
            text = (text ?? "").Trim();
            string lower = text.ToLowerInvariant();
            me = (me ?? "").ToLowerInvariant();
            foreach (var call in new[] { "@" + me, me, "all ships", "everyone", "wingmen" })
            {
                if (call.Length == 0 || !lower.StartsWith(call)) continue;
                // (the whole word: "Wingmanly" isn't to "Wingman")
                if (lower.Length > call.Length && char.IsLetterOrDigit(lower[call.Length])) continue;
                return text.Substring(call.Length).TrimStart(',', ':', ' ', '!', ';', '-');
            }
            return null;
        }

        /// <summary>Carry out an order in words; the answer to give (null: talk, not an order).</summary>
        public string Command(string words, int fromId)
        {
            string w = (words ?? "").Trim();
            string lw = w.ToLowerInvariant();
            string Arg(params string[] verbs)
            {
                foreach (var v in verbs)
                    if (lw == v || lw.StartsWith(v + " ")) return w.Substring(v.Length).Trim().TrimEnd('.', '!');
                return null;
            }
            string a;
            if ((a = Arg("follow", "escort", "form up on", "form on")) != null) return Order("follow", a.Length == 0 || a.ToLowerInvariant() == "me" ? "" : a, fromId);
            if (Arg("regroup", "form up", "come here", "return", "rtb") != null) return Order("follow", "", fromId);
            if (Arg("hold", "stop", "wait", "stay") != null) return Order("hold", "", fromId);
            if ((a = Arg("attack", "engage", "fire on", "kill")) != null) return Order("attack", a.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? a.Substring(4) : a, fromId);
            if ((a = Arg("warp to", "go to", "goto", "jump to", "warp")) != null) return Order("goto", a, fromId);
            if (Arg("dock") != null) return Order("dock", "", fromId);
            if (Arg("status", "report", "sitrep") != null) return Report();
            if (Arg("help", "orders") != null) return "orders: follow me / follow <name>, hold, attack [target], warp to <place>, dock, regroup, status.";
            return null;
        }

        /// <summary>Give the pilot an order (from the comms or the API); the answer. `fromId`: who
        /// gave it (0 from the API: "follow" with no name is then the last player to speak).</summary>
        public string Order(string order, string target, int fromId = 0)
        {
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return "not ready yet";
            var cc = Pilot.demo.carrier;
            target = (target ?? "").Trim();
            string o = (order ?? "").Trim().ToLowerInvariant();
            switch (o)
            {
                case "follow":
                case "regroup":
                {
                    var c = CoopClient.Instance;
                    CoopPlayer who = null;
                    if (c != null)
                    {
                        if (target.Length > 0 && target.ToLowerInvariant() != "me")
                        {
                            who = c.Players.FirstOrDefault(p => p != null && p.id != c.MyId && p.name.ToLowerInvariant().Contains(target.ToLowerInvariant()));
                            if (who == null) return $"there's no player called {target}.";
                        }
                        else if (o == "regroup" && fromId == 0) who = NearestHuman(cc.transform.position);
                        else who = c.Players.FirstOrDefault(p => p != null && p.id == (fromId != 0 ? fromId : lastSpeaker) && p.id != c.MyId);
                        if (who == null) who = NearestHuman(cc.transform.position);
                    }
                    if (who == null) return "there's nobody to follow.";
                    Pilot.Follow(who.id);
                    lastLeader = who.id;
                    Orders++;
                    return $"following {who.name}.";
                }
                case "hold":
                    Pilot.Hold();
                    lastLeader = 0;
                    Orders++;
                    return "holding position.";
                case "attack":
                {
                    var t = target.Length > 0 ? Wingman.FindTarget(target, cc.transform.position) : Wingman.NearestEnemy(cc.transform.position, 30000f);
                    if (t == null) return target.Length > 0 ? $"no target called {target} in sight." : "no enemies in sight.";
                    Pilot.Attack(t);
                    Orders++;
                    return $"engaging {Wingman.NameOf(t)}.";
                }
                case "goto":
                {
                    var p = Place(target);
                    if (p == null) return target.Length > 0 ? $"don't know a place called {target}." : "go where?";
                    Pilot.Goto(p);
                    lastLeader = 0;
                    Orders++;
                    return $"heading for {p.displayName}.";
                }
                case "dock":
                    Pilot.Dock();
                    lastLeader = 0;
                    Orders++;
                    return "docking.";
                default:
                    return $"don't understand '{order}'.";
            }
        }

        /// <summary>The nearest player who isn't a bot (seen in this level first), or null.</summary>
        CoopPlayer NearestHuman(Vector3 from)
        {
            var w = CoopWorld.Instance;
            CoopPlayer best = null;
            float bd = float.MaxValue;
            foreach (var p in Humans())
            {
                var r = w != null ? w.RemoteOf(p.id) : null;
                float d = r != null && !r.Hidden ? (r.transform.position - from).sqrMagnitude : float.MaxValue / 2f;
                if (best == null || d < bd) { best = p; bd = d; }
            }
            return best;
        }

        static WarpPoint Place(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = name.Trim().TrimEnd('.', '!').ToLowerInvariant();
            if (n.StartsWith("the ")) n = n.Substring(4);
            var all = WarpPoint.All.Where(w => w != null && !string.IsNullOrEmpty(w.displayName)).ToList();
            return all.FirstOrDefault(w => w.displayName.ToLowerInvariant() == n)
                   ?? all.FirstOrDefault(w => w.displayName.ToLowerInvariant().StartsWith(n))
                   ?? all.FirstOrDefault(w => w.displayName.ToLowerInvariant().Contains(n));
        }

        static float Fraction(Health h) => h != null && h.maxHealth > 0f ? Mathf.Clamp01(h.Current / h.maxHealth) : 1f;

        string Report()
        {
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return "not ready yet";
            var cc = Pilot.demo.carrier;
            var sh = cc.GetComponentInChildren<Shield>();
            var near = WarpPoint.Nearest(cc.transform.position);
            string missiles = cc.missiles == null ? "" : cc.missiles.Stock < 0 ? ", missiles ready" : $", {cc.missiles.Stock} missiles";
            return $"{cc.shipClass} {cc.shipName}, hull {Fraction(cc.GetComponent<Health>()):P0}, shield {(sh != null ? sh.Strength / Mathf.Max(1f, sh.capacity) : 0f):P0}" +
                   $"{missiles}; {Pilot.Doing}{(near != null ? $", near {near.displayName}" : "")}.";
        }

        // ------------------------------------------------------------ the API

        void StartApi()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log("[CoopBot] no API in a web page");
#else
            try
            {
                http = new HttpListener();
                http.Prefixes.Add($"http://127.0.0.1:{Port}/");
                http.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoopBot] no API on port {Port}: {e.Message}");
                http = null;
                return;
            }
            var h = http;
            listener = new Thread(() =>
            {
                while (h.IsListening)
                {
                    try
                    {
                        var ctx = h.GetContext();
                        string body = "";
                        if (ctx.Request.HasEntityBody)
                            using (var r = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                        requests.Enqueue((ctx, body));
                    }
                    catch (Exception) { if (!h.IsListening) break; }
                }
            }) { IsBackground = true, Name = "CoopBot API" };
            listener.Start();
            Debug.Log($"[CoopBot] API on http://127.0.0.1:{Port}/");
#endif
        }

        void StopApi()
        {
            var h = http;
            http = null;
            if (h == null) return;
            try { h.Stop(); h.Close(); } catch (Exception) { }
        }

        [Serializable] class SayBody { public string text; }
        [Serializable] class OrderBody { public string order, target; }

        void Serve(HttpListenerContext ctx, string body)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
            string method = ctx.Request.HttpMethod;
            string json;
            int code = 200;
            try
            {
                bool post = method == "POST";
                // (only programs on this machine: a web page's request carries an Origin, and a
                // page can't send a JSON post here without a preflight this never answers)
                bool fromPage = !string.IsNullOrEmpty(ctx.Request.Headers["Origin"]);
                bool notJson = post && (ctx.Request.ContentType == null || !ctx.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase));
                if (fromPage) { code = 403; json = "{\"error\":\"not from a web page\"}"; }
                else if (notJson) { code = 415; json = "{\"error\":\"send JSON (Content-Type: application/json)\"}"; }
                else switch (path)
                {
                    case "/status" when !post: json = StatusJson(); break;
                    case "/messages" when !post:
                        int.TryParse(ctx.Request.QueryString["after"], out int after);
                        json = MessagesJson(after);
                        break;
                    case "/destinations" when !post: json = DestinationsJson(); break;
                    case "/say" when post:
                    {
                        var b = Parse<SayBody>(body);
                        bool ok = b != null && CoopChat.Say(b.text);
                        json = ok ? "{\"ok\":true}" : "{\"ok\":false,\"error\":" + Q(CoopClient.Active ? "nothing to say" : "not connected to a co-op game") + "}";
                        break;
                    }
                    case "/order" when post:
                    {
                        var b = Parse<OrderBody>(body);
                        int before = Orders;
                        string answer = Order(b?.order, b?.target);
                        bool ok = Orders > before;
                        if (ok && CoopClient.Active) CoopChat.Say($"{Me}: {answer}");
                        json = $"{{\"ok\":{B(ok)},\"answer\":{Q(answer)}}}";
                        break;
                    }
                    case "/quit" when post:
                        json = "{\"ok\":true}";
                        Invoke(nameof(Quit), 0.5f);
                        break;
                    case "/status":
                    case "/messages":
                    case "/destinations":
                    case "/say":
                    case "/order":
                    case "/quit":
                        code = 405;
                        json = "{\"error\":" + Q($"{path} doesn't take {method}") + "}";
                        break;
                    default: code = 404; json = "{\"error\":\"no such call\"}"; break;
                }
            }
            catch (Exception e) { code = 500; json = "{\"error\":" + Q(e.Message) + "}"; }
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode = code;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception) { }
        }

        static T Parse<T>(string body) where T : class
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try { return JsonUtility.FromJson<T>(body); }
            catch (Exception) { return null; }
        }

        void Quit()
        {
            Debug.Log("[CoopBot] told to quit");
            Application.Quit();
        }

        /// <summary>A string as JSON.</summary>
        public static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static string B(bool b) => b ? "true" : "false";
        static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? "0" : f.ToString("0.###", CultureInfo.InvariantCulture);

        string StatusJson()
        {
            var sb = new StringBuilder("{");
            var c = CoopClient.Instance;
            sb.Append($"\"name\":{Q(Me)},\"connected\":{B(CoopClient.Active)},\"level\":{Q(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)}");
            var cc = Pilot != null && Pilot.demo != null ? Pilot.demo.carrier : null;
            if (cc != null)
            {
                var sh = cc.GetComponentInChildren<Shield>();
                var crew = cc.GetComponent<Crew>();
                var drive = cc.GetComponent<WarpDrive>();
                var near = WarpPoint.Nearest(cc.transform.position);
                sb.Append($",\"ship\":{{\"class\":{Q(FrigateClass.Of(cc))},\"name\":{Q(cc.shipName)},\"kind\":{Q(cc.shipClass)},\"destroyed\":{B(cc.Destroyed)}");
                sb.Append($",\"hull\":{F(Fraction(cc.GetComponent<Health>()))},\"shield\":{F(sh != null ? sh.Strength / Mathf.Max(1f, sh.capacity) : 0f)}");
                if (crew != null) sb.Append($",\"crew\":{crew.Aboard},\"complement\":{crew.complement}");
                if (cc.missiles != null) sb.Append($",\"missiles\":{cc.missiles.Stock}");
                sb.Append($",\"speed\":{F(cc.Velocity.magnitude)},\"warp\":{Q(drive != null ? drive.Current.ToString().ToLowerInvariant() : "none")}");
                sb.Append($",\"docked\":{B(Docking.Docked)},\"landed\":{B(PlanetLanding.Busy)}");
                if (near != null) sb.Append($",\"near\":{Q(near.displayName)},\"near_km\":{F(Vector3.Distance(cc.transform.position, near.Position) / 1000f)}");
                sb.Append("}");
                sb.Append($",\"order\":{{\"order\":{Q(Pilot.Current.ToString().ToLowerInvariant())},\"leader\":{Q(Pilot.LeaderName)}");
                sb.Append($",\"target\":{Q(Wingman.NameOf(Pilot.Target))},\"destination\":{Q(Pilot.Destination != null ? Pilot.Destination.displayName : "")},\"doing\":{Q(Pilot.Doing)}}}");
                // what is near, enemies first
                sb.Append(",\"nearby\":[");
                bool first = true;
                Vector3 at = cc.transform.position;
                foreach (var t in Targets.All.Where(t => t != null && t.gameObject.activeInHierarchy && (t.position - at).sqrMagnitude < 50000f * 50000f)
                             .OrderBy(t => t.TryGetComponent(out TargetInfo i) && i.Hostile ? 0 : 1).ThenBy(t => (t.position - at).sqrMagnitude).Take(12))
                {
                    var info = t.GetComponent<TargetInfo>();
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"{{\"name\":{Q(Wingman.NameOf(t))},\"hostile\":{B(info != null && info.Hostile)},\"km\":{F(Vector3.Distance(t.position, at) / 1000f)},\"hull\":{F(Fraction(t.GetComponent<Health>()))}}}");
                }
                sb.Append("]");
            }
            if (c != null)
            {
                sb.Append(",\"players\":[");
                bool first = true;
                foreach (var p in c.Players)
                {
                    if (p == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    var r = CoopWorld.Instance != null ? CoopWorld.Instance.RemoteOf(p.id) : null;
                    float km = r != null && !r.Hidden && cc != null ? Vector3.Distance(r.transform.position, cc.transform.position) / 1000f : -1f;
                    sb.Append($"{{\"id\":{p.id},\"name\":{Q(p.name)},\"me\":{B(p.id == c.MyId)},\"bot\":{B(IsBot(p))},\"level\":{Q(p.level)},\"km\":{F(km)}}}");
                }
                sb.Append("]");
            }
            return sb.Append("}").ToString();
        }

        string MessagesJson(int after)
        {
            var c = CoopClient.Instance;
            var sb = new StringBuilder($"{{\"last\":{CoopChat.LastSeq},\"messages\":[");
            bool first = true;
            foreach (var l in CoopChat.Lines)
            {
                if (l.seq <= after) continue;
                if (!first) sb.Append(',');
                first = false;
                bool mine = c != null && l.from == c.MyId;
                bool toMe = !mine && AddressedPart(l.text, Me) != null;
                sb.Append($"{{\"seq\":{l.seq},\"from\":{Q(l.name)},\"mine\":{B(mine)},\"to_me\":{B(toMe)},\"text\":{Q(l.text)}}}");
            }
            return sb.Append("]}").ToString();
        }

        string DestinationsJson()
        {
            var cc = Pilot != null && Pilot.demo != null ? Pilot.demo.carrier : null;
            Vector3 at = cc != null ? cc.transform.position : Vector3.zero;
            var sb = new StringBuilder("{\"destinations\":[");
            bool first = true;
            foreach (var w in WarpPoint.All.Where(w => w != null).OrderBy(w => (w.Position - at).sqrMagnitude))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"{{\"name\":{Q(w.displayName)},\"kind\":{Q(string.IsNullOrEmpty(w.kindLabel) ? w.kind.ToString() : w.kindLabel)},\"hostile\":{B(w.hostile)},\"km\":{F(Vector3.Distance(w.Position, at) / 1000f)}}}");
            }
            return sb.Append("]}").ToString();
        }
    }
}
