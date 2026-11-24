using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
    /// "Wingman, mine asteroids" (and sell the ore when the hold is full), "Wingman, sell",
    /// "Wingman status" ("all ships", "everyone" or "wingmen" orders every bot). It answers, and
    /// says when it starts a fight, finishes one, arrives or can't do what it was told.
    ///
    /// It also answers on a small HTTP API on this machine only (127.0.0.1, -botport, 7781 by
    /// default), for an AI to fly it (Tools/CoopAI/coop_ai_mcp.py makes it an MCP server):
    ///   GET  /status            its ship, its order, what is near it, the players
    ///   GET  /messages?after=N  the comms channel after message N
    ///   GET  /destinations      the places to warp to, nearest first
    ///   POST /say   {text}      say something on the comms channel
    ///   POST /order {order, target}  follow|hold|attack|goto|regroup|dock|mine|sell
    ///   GET  /chatmode          who answers talk that isn't an order: {mode, listener_seen_s}
    ///   POST /chatmode {mode}   llm|personality|paused (or on|off|pause|resume)
    ///   POST /quit              close the game
    /// Talk to it that isn't an order goes by its chat mode (-chatmode, "Wingman, chat on|off|pause|
    /// resume", or the API; kept between runs): llm, an LLM reading the channel answers it (the
    /// built-in personality, WingmanPersonality, while none is reading); personality, the built-in
    /// personality always; paused, nobody (a "standing by" line at most every 30 s).
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
        // when something last read the channel as a listener (a person, or Claude through the MCP
        // server): talk that isn't an order is left for it to answer
        float listenerSeen = -1000f, toldOrdersOnly = -1000f;
        /// <summary>Something is reading the channel to answer talk (it read it in the last 90 s).</summary>
        public bool Listened => Time.realtimeSinceStartup - listenerSeen < 90f;
        /// <summary>Seconds since a listener last read the channel (0 while one waits on it; -1 never).</summary>
        public float ListenerSeenSeconds => listenerSeen < -999f ? -1f : Mathf.Max(0f, Time.realtimeSinceStartup - listenerSeen);

        /// <summary>Who answers talk to the bot that isn't an order. Llm: the LLM listener reading
        /// the channel (/messages?listener=1), or the built-in personality while none has read it
        /// for 90 s. Personality: the built-in personality (WingmanPersonality), never waiting on
        /// the LLM. Paused: nobody; a "standing by" line at most every 30 s. Orders always work.</summary>
        public enum ChatMode { Llm, Personality, Paused }
        public ChatMode Chat { get; private set; } = ChatMode.Llm;
        // (what "resume" goes back to)
        ChatMode beforePause = ChatMode.Llm;
        float toldPaused = -1000f;
        WingmanPersonality personality;
        bool personalityLoaded;
        const string ChatPref = "CoopBot.ChatMode.";
        // /messages calls waiting for a new message (long polls): answered when one comes, or at their deadline
        readonly List<(HttpListenerContext ctx, int after, float until)> waiting = new List<(HttpListenerContext, int, float)>();
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
            LoadChatMode();
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

        /// <summary>An AI player, a test bot or a scripted wingman (the reel's): never a leader to form up on.</summary>
        public static bool IsBot(CoopPlayer p) => p.client == ClientName || p.client == "bot" || p.client == "wingman";

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
                    Pilot.Sold += what => { if (CoopClient.Active) CoopChat.Say($"{Me}: {what}."); };
                    if (lastLeader != 0) Pilot.Follow(lastLeader);
                }
            }
            if (!greeted && CoopClient.Active && Pilot != null)
            {
                greeted = true;
                var human = Humans().FirstOrDefault();
                if (human != null) { CallOffChain(); Pilot.Follow(human.id); lastLeader = lastSpeaker = human.id; }
                CoopChat.Say($"{Me} on station{(human != null ? $", forming up on {human.name}" : "")}. Orders by name: \"{Me}, follow me / hold / attack / warp to <place> / dock / mine / sell / cargo / give credits / status\".");
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
            // waiting over a world for a landed leader: said once a landing (not again after each
            // fight while it waits)
            var over = Pilot != null ? Pilot.WaitingOver : null;
            if (over != lastWaitingOver)
            {
                lastWaitingOver = over;
                if (over != null && CoopClient.Active) CoopChat.Say($"{Me}: {Pilot.WaitingLine}.");
            }
            // a fight starting is said once, whatever it says it is doing on the way in
            bool fighting = Pilot != null && Pilot.Fighting;
            if (fighting && !wasFighting && CoopClient.Active)
            {
                var info = Pilot.Target.GetComponent<TargetInfo>();
                CoopChat.Say($"{Me}: engaging {(info != null && !string.IsNullOrEmpty(info.Name) ? info.Name : Pilot.Target.name)}.");
            }
            wasFighting = fighting;
            NextInChain();
            while (requests.TryDequeue(out var r)) Serve(r.ctx, r.body);
            CoopGifts.Tick();
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                var (ctx, after, until) = waiting[i];
                if (CoopChat.LastSeq <= after && Time.realtimeSinceStartup < until) continue;
                waiting.RemoveAt(i);
                Respond(ctx, 200, MessagesJson(after));
            }
        }

        WarpPoint lastWaitingOver;

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
            CallOffChain();
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
            if (!string.IsNullOrEmpty(reply))
            {
                // (marked as carried out, for the listener: it isn't to order it a second time)
                Handled(line.seq);
                CoopChat.Say($"{Me}: {reply}");
                return;
            }
            // talk, not an order: by the chat mode
            float now = Time.realtimeSinceStartup;
            switch (Chat)
            {
                case ChatMode.Paused:
                    // (standing by: said at most once in half a minute)
                    if (now - toldPaused > 30f)
                    {
                        toldPaused = now;
                        CoopChat.Say($"{Me}: {Personality?.PausedLine(Facts(line.from)) ?? "comms quiet, standing by. Orders still work."}");
                    }
                    return;
                case ChatMode.Llm when Listened:
                    return;        // (the listener answers it)
            }
            string said = Personality?.Reply(line.name, rest, now, Facts(line.from));
            if (!string.IsNullOrEmpty(said)) CoopChat.Say($"{Me}: {said}");
            // (no personality to talk with: say so, not more than once in half a minute)
            else if (now - toldOrdersOnly > 30f)
            {
                toldOrdersOnly = now;
                CoopChat.Say($"{Me}: nobody on comms here, orders only: follow, hold, attack, warp to, dock, mine, sell, cargo, give credits, status.");
            }
        }

        WingmanPersonality Personality
        {
            get
            {
                if (!personalityLoaded) { personalityLoaded = true; personality = WingmanPersonality.Load(); }
                return personality;
            }
        }

        // ------------------------------------------------------------ the chat mode

        public static string ModeName(ChatMode m) => m == ChatMode.Llm ? "llm" : m == ChatMode.Personality ? "personality" : "paused";

        /// <summary>A chat mode by name or by switch word: llm/on, personality/off, paused/pause;
        /// resume/unpause is `beforePause`. False if the word is none of them.</summary>
        public static bool ParseChatMode(string word, ChatMode beforePause, out ChatMode mode)
        {
            switch ((word ?? "").Trim().ToLowerInvariant())
            {
                case "llm": case "on": case "ai": mode = ChatMode.Llm; return true;
                case "personality": case "off": case "builtin": case "built-in": mode = ChatMode.Personality; return true;
                case "paused": case "pause": mode = ChatMode.Paused; return true;
                case "resume": case "unpause": case "unpaused":
                    mode = beforePause == ChatMode.Paused ? ChatMode.Llm : beforePause;
                    return true;
                default: mode = ChatMode.Llm; return false;
            }
        }

        static bool IsResume(string word)
        {
            string w = (word ?? "").Trim().ToLowerInvariant();
            return w == "resume" || w == "unpause" || w == "unpaused";
        }

        /// <summary>Switch the chat mode by a word (see ParseChatMode); "resume" when not paused
        /// changes nothing. False if the word isn't a mode.</summary>
        public bool SetChatMode(string word)
        {
            if (!ParseChatMode(word, beforePause, out var m)) return false;
            if (IsResume(word) && Chat != ChatMode.Paused) return true;
            SetChatMode(m);
            return true;
        }

        public void SetChatMode(ChatMode m)
        {
            if (m == ChatMode.Paused && Chat != ChatMode.Paused) beforePause = Chat;
            if (m != Chat) toldPaused = -1000f;
            Chat = m;
            SaveChatMode();
        }

        // (kept between runs, per bot name, in this machine's player prefs: the bot's HOME on a server)
        static string ChatPrefKey => ChatPref + (LaunchArgs.ValueOf("-name") ?? "Wingman");

        void LoadChatMode()
        {
            // the launch flag, or what it was when it last ran, or llm
            string flag = LaunchArgs.ValueOf("-chatmode");
            if (flag != null && ParseChatMode(flag, ChatMode.Llm, out var m)) { Chat = m; return; }
            if (flag != null) Debug.LogWarning($"[CoopBot] -chatmode {flag}: not llm, personality or paused");
            try
            {
                string saved = PlayerPrefs.GetString(ChatPrefKey, "");
                string[] parts = saved.Split(':');
                if (ParseChatMode(parts[0], ChatMode.Llm, out m) && !IsResume(parts[0])) Chat = m;
                if (parts.Length > 1 && ParseChatMode(parts[1], ChatMode.Llm, out var b) && b != ChatMode.Paused) beforePause = b;
            }
            catch (Exception) { }
            Debug.Log($"[CoopBot] chat mode {ModeName(Chat)}");
        }

        void SaveChatMode()
        {
            if (!Application.isPlaying) return;
            try
            {
                PlayerPrefs.SetString(ChatPrefKey, ModeName(Chat) + ":" + ModeName(beforePause));
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        static readonly Regex ChatOrderRx = new Regex(@"^(?:chat|chatter|comms)(?:\s+mode)?(?:\s+(?<arg>[a-z\-]+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly string[] ChatArgs = { "", "status", "on", "off", "pause", "paused", "resume", "unpause", "unpaused", "llm", "ai", "personality", "builtin", "built-in" };

        /// <summary>An order about the chat mode ("chat on|off|pause|resume", "chat mode",
        /// "chat mode personality"): its word ("" to ask what it is), or null if it isn't one.</summary>
        public static string ChatOrderArg(string words)
        {
            var m = ChatOrderRx.Match((words ?? "").Trim().TrimEnd('?', '!', '.').TrimEnd());
            if (!m.Success) return null;
            string arg = m.Groups["arg"].Success ? m.Groups["arg"].Value.ToLowerInvariant() : "";
            // ("comms check" isn't one)
            return Array.IndexOf(ChatArgs, arg) >= 0 ? (arg == "status" ? "" : arg) : null;
        }

        /// <summary>The chat mode, said on the comms.</summary>
        public string ChatModeLine()
        {
            switch (Chat)
            {
                case ChatMode.Llm:
                    return Listened ? "chat mode llm: talk goes to the listener." : "chat mode llm: no listener on now, I'll answer talk myself till one is.";
                case ChatMode.Personality:
                    return "chat mode personality: LLM off, I answer talk myself.";
                default:
                    return $"chat mode paused: standing by, orders only. \"{Me}, chat resume\" to resume.";
            }
        }

        /// <summary>May this player switch the chat mode? Its leader or the room's host, when it
        /// has either (other than itself); otherwise anyone.</summary>
        public bool MayChangeChat(int fromId)
        {
            var c = CoopClient.Instance;
            int leader = Pilot != null ? Pilot.LeaderId : 0;
            int host = c != null && c.HostId != c.MyId ? c.HostId : 0;
            if (leader == 0 && host == 0) return true;
            return fromId != 0 && (fromId == leader || fromId == host);
        }

        string ChatOrder(string arg, int fromId)
        {
            if (arg.Length == 0) return ChatModeLine();
            if (!MayChangeChat(fromId))
            {
                var c = CoopClient.Instance;
                var who = new List<string>();
                if (Pilot != null && Pilot.LeaderId != 0) who.Add(Pilot.LeaderName);
                if (c != null && c.HostId != 0 && c.HostId != c.MyId) who.Add(c.NameOf(c.HostId));
                return $"chat mode stays {ModeName(Chat)}: only {string.Join(" or ", who.Distinct())} can switch it.";
            }
            var was = Chat;
            SetChatMode(arg);
            return Chat == was ? $"chat mode already {ModeName(Chat)}." : ChatModeLine();
        }

        /// <summary>The live facts the personality's lines are filled in from (empty: not known now).</summary>
        Dictionary<string, string> Facts(int fromId)
        {
            var c = CoopClient.Instance;
            var f = new Dictionary<string, string>
            {
                ["me"] = Me,
                ["player"] = c != null ? c.NameOf(fromId) : "",
                ["order"] = Pilot != null ? Pilot.Doing : "",
                ["leader"] = Pilot != null ? Pilot.LeaderName : "",
            };
            var cc = Pilot != null && Pilot.demo != null ? Pilot.demo.carrier : null;
            if (cc == null) return f;
            string Pct(float x) => Mathf.RoundToInt(Mathf.Clamp01(x) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            var sh = cc.GetComponentInChildren<Shield>();
            f["hull"] = Pct(Fraction(cc.GetComponent<Health>()));
            f["shield"] = Pct(sh != null ? sh.Strength / Mathf.Max(1f, sh.capacity) : 0f);
            f["ship"] = $"{cc.shipClass} {cc.shipName}".Trim();
            var near = WarpPoint.Nearest(cc.transform.position);
            f["near"] = near != null ? near.displayName : "";
            var hold = cc.GetComponentInChildren<CargoHold>();
            if (hold != null)
            {
                f["hold"] = $"{hold.Used:F0} of {hold.capacity:F0} t";
                f["ore"] = $"{hold.OreTonnes:F0} t";
                f["credits"] = hold.Credits.ToString("N0", CultureInfo.InvariantCulture) + " cr";
            }
            // the hostiles on the scope (30 km), nearest first
            Vector3 at = cc.transform.position;
            var hostile = Targets.All.Where(t => t != null && t.gameObject.activeInHierarchy && t.TryGetComponent(out TargetInfo i) && i.Hostile
                                                 && !(t.TryGetComponent(out Health h) && h.Dead) && (t.position - at).sqrMagnitude < 30000f * 30000f)
                                     .OrderBy(t => (t.position - at).sqrMagnitude).ToList();
            f["hostiles"] = hostile.Count == 0 ? "nothing hostile"
                : $"{hostile.Count} hostile{(hostile.Count == 1 ? "" : "s")}, nearest {Wingman.NameOf(hostile[0])} at {Vector3.Distance(hostile[0].position, at) / 1000f:F1} km";
            if (Pilot.Fighting) f["target"] = Wingman.NameOf(Pilot.Target);
            f["docked"] = Docking.Docked ? "docked" : "";
            return f;
        }

        // messages the bot carried out as orders (by number), marked "handled" for the listener
        readonly HashSet<int> handled = new HashSet<int>();

        void Handled(int seq)
        {
            handled.Add(seq);
            if (handled.Count > 400) handled.RemoveWhere(n => n < seq - 300);
        }

        /// <summary>Was message `seq` carried out by the bot as an order?</summary>
        public bool WasHandled(int seq) => handled.Contains(seq);

        // words said before the name ("hey wingman, ..."), spoken or typed
        static readonly string[] Greetings = { "hey", "hi", "ok", "okay", "oi", "right" };

        /// <summary>What follows the bot's name (or "@name", "all ships", "everyone", "wingmen") at
        /// the start of a message (after a "hey" or "ok"); null if the message isn't to it.</summary>
        public static string AddressedPart(string text, string me)
        {
            text = (text ?? "").Trim();
            foreach (var g in Greetings)
                if (text.Length > g.Length && text.StartsWith(g, StringComparison.OrdinalIgnoreCase) && !char.IsLetterOrDigit(text[g.Length]))
                {
                    text = text.Substring(g.Length).TrimStart(',', ' ', '!');
                    break;
                }
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

        /// <summary>Carry out an order in words; the answer to give (null: talk, not an order).
        /// Several orders in one message ("mine, then sell", "warp to Heaven and dock") are carried
        /// out one after another (Chain).</summary>
        public string Command(string words, int fromId)
        {
            var chain = Clauses(words);
            if (chain != null) return Chain(chain, fromId);
            var p = ParseOrder(words);
            if (p == null) return null;
            // (a new order that takes time: whatever was still to come of the last message's is
            // called off; a report or credits on the way leave it going)
            if (!Instant(p.Value.kind)) CallOffChain();
            return Run(p.Value, fromId);
        }

        static readonly string[] PoliteLeads = { "please", "can you", "could you", "would you", "will you", "go and", "now" };
        static readonly string[] PoliteTails = { "please", "now", "for me", "for us", "thanks", "thank you" };

        /// <summary>An order without its politeness: "can you go mine please" is "mine". Leading
        /// "please", "can you", "could you", "would you", "will you", "now", "go" (not "go to" or
        /// "go mining", which are orders), and trailing "please", "now", "for me", "thanks".</summary>
        public static string Plain(string words)
        {
            string w = (words ?? "").Trim();
            for (bool changed = true; changed;)
            {
                changed = false;
                string t = w.TrimEnd('?', '!', '.', ',', ' ');
                foreach (var lead in PoliteLeads)
                    if (t.Length > lead.Length + 1 && t.StartsWith(lead, StringComparison.OrdinalIgnoreCase) && (t[lead.Length] == ' ' || t[lead.Length] == ','))
                    { t = t.Substring(lead.Length + 1).TrimStart(',', ' '); changed = true; }
                if (t.StartsWith("go ", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("go to ", StringComparison.OrdinalIgnoreCase)
                    && !t.StartsWith("go mining", StringComparison.OrdinalIgnoreCase))
                { t = t.Substring(3).TrimStart(); changed = true; }
                foreach (var tail in PoliteTails)
                    if (t.Length > tail.Length + 1 && t.EndsWith(" " + tail, StringComparison.OrdinalIgnoreCase))
                    { t = t.Substring(0, t.Length - tail.Length - 1).TrimEnd(',', ' '); changed = true; }
                if (changed) w = t;
            }
            return w;
        }

        // words after "mine" that aren't a field's name ("mine some asteroids", "mine the nearest rocks")
        static readonly HashSet<string> MineFiller = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "some", "any", "in", "at", "nearest", "closest", "near", "nearby", "asteroids", "asteroid",
            "rocks", "rock", "field", "ore", "for", "there", "here", "it", "them", "minerals", "belt",
        };

        /// <summary>The field's name in what follows "mine" ("at the Inner Belt": "Inner Belt"), or
        /// "" for the nearest field ("mine", "mine some asteroids", "mine the nearest rocks").</summary>
        public static string MineFieldName(string target)
        {
            string name = (target ?? "").Trim().TrimEnd('.', '!', '?');
            foreach (var lead in new[] { "the asteroids", "asteroids", "rocks", "in", "at", "the" })
                if (name.Equals(lead, StringComparison.OrdinalIgnoreCase)) name = "";
                else if (name.StartsWith(lead + " ", StringComparison.OrdinalIgnoreCase)) name = name.Substring(lead.Length + 1).Trim();
            var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.All(x => MineFiller.Contains(x)) ? "" : name;
        }

        /// <summary>An order in words, parsed (none: talk): its kind ("chat", "follow", "regroup",
        /// "cargo", "give", "hold", "attack", "goto", "dock", "mine", "sell", "status", "help") and
        /// what it is to (a name, a place, an amount).</summary>
        public static (string kind, string arg)? ParseOrder(string words)
        {
            // ("can you mine please": "mine")
            string w = Plain(words).Trim().TrimEnd('?', '!', '.').TrimEnd();
            string lw = w.ToLowerInvariant();
            string Arg(params string[] verbs)
            {
                foreach (var v in verbs)
                    if (lw == v || lw.StartsWith(v + " ")) return w.Substring(v.Length).Trim().TrimEnd('.', '!');
                return null;
            }
            string NoThe(string x) => x.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? x.Substring(4) : x;
            string a;
            if ((a = ChatOrderArg(w)) != null) return ("chat", a);
            if ((a = Arg("follow", "escort", "form up on", "form on")) != null) return ("follow", a.Length == 0 || a.ToLowerInvariant() == "me" ? "" : a);
            if (Arg("regroup", "form up", "come here", "return", "rtb") != null) return ("regroup", "");
            // (before "hold": "hold report", "what's in your hold")
            if (Arg("cargo", "manifest", "inventory", "hold report", "report hold", "hold status", "what's in your hold", "whats in your hold",
                    "what is in your hold", "what's in the hold", "whats in the hold", "what is in the hold", "how full is your hold", "how full is the hold",
                    "what are you carrying", "check your hold", "check the hold") != null)
                return ("cargo", "");
            if ((a = GiveArgs(w.TrimEnd('?', '.', '!'))) != null) return ("give", a);
            if (Arg("hold", "stop", "wait", "stay") != null) return ("hold", "");
            if ((a = Arg("attack", "engage", "fire on", "kill")) != null) return ("attack", NoThe(a));
            if ((a = Arg("warp to", "go to", "goto", "jump to", "warp")) != null) return ("goto", a);
            if (Arg("dock") != null) return ("dock", "");
            if ((a = Arg("start mining", "go mining", "mine asteroids", "mine at", "mine in", "mine")) != null) return ("mine", NoThe(a));
            // ("sell at the nearest station", "sell it": wherever the nearest market is)
            if (Arg("sell the ore", "sell ore", "sell") != null) return ("sell", "");
            if (Arg("status", "report", "sitrep") != null) return ("status", "");
            if (Arg("help", "orders") != null) return ("help", "");
            return null;
        }

        string Run((string kind, string arg) p, int fromId)
        {
            switch (p.kind)
            {
                case "chat": return ChatOrder(p.arg, fromId);
                case "regroup": return Order("follow", "", fromId);
                case "status": return Report();
                case "help": return "orders: follow me / follow <name>, hold, attack [target], warp to <place>, dock, regroup, mine [at <field>], sell, cargo, give [N] credits [to <name>], status; chat on|off|pause|resume. Several at once: \"warp to Heaven, then dock\".";
                default: return Order(p.kind, p.arg, fromId);
            }
        }

        // ---------------------------------------------------------------- several orders at once

        static readonly Regex ThenRx = new Regex(@"\s*(?:,\s*and\s+then\b|,\s*then\b|\band\s+then\b|\bthen\b|;|,\s*and\b|\band\b|,(?!\d))\s*", RxOpt);

        /// <summary>A message of several orders ("mine and then sell", "warp to Heaven, dock"),
        /// each parsed; null if it is one order, or isn't all orders (talk, or a name with "and" in
        /// it: taken whole).</summary>
        public static List<(string kind, string arg)> Clauses(string words)
        {
            string w = (words ?? "").Trim().TrimEnd('?', '!', '.').TrimEnd();
            var parts = ThenRx.Split(w).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Count < 2) return null;
            var list = new List<(string kind, string arg)>();
            foreach (var part in parts)
            {
                var p = ParseOrder(part);
                if (p == null) return null;
                list.Add(p.Value);
            }
            return list;
        }

        /// <summary>Orders that are over at once (said, or handed over), and those that are never
        /// over till another order comes (nothing can come after them).</summary>
        static bool Instant(string kind) => kind == "chat" || kind == "cargo" || kind == "give" || kind == "status" || kind == "help";
        static bool Standing(string kind) => kind == "follow" || kind == "regroup" || kind == "hold";

        // the orders still to come of a message's chain, and how the one being carried out is
        // going: 0 still going, 1 done, -1 it couldn't (the rest called off)
        readonly List<(string kind, string arg, int from)> pending = new List<(string, string, int)>();
        Func<int> stepDone;
        string stepWords = "";

        /// <summary>Whatever was still to come of a chain, called off (a new order, the bot forming up by itself).</summary>
        void CallOffChain()
        {
            pending.Clear();
            stepDone = null;
        }

        /// <summary>The orders still to come (the tests).</summary>
        public int Pending => pending.Count;

        string Chain(List<(string kind, string arg)> orders, int fromId)
        {
            // (mining sells when the hold is full: "mine and sell" is mining)
            for (int i = orders.Count - 1; i > 0; i--)
                if (orders[i].kind == "sell" && orders[i - 1].kind == "mine") orders.RemoveAt(i);
            // (one that goes on till told otherwise is over as soon as another order comes:
            // "stop and dock" is docking, "come here and follow me" following)
            for (int i = orders.Count - 2; i >= 0; i--)
                if (Standing(orders[i].kind) && orders.Skip(i + 1).Any(x => !Instant(x.kind))) orders.RemoveAt(i);
            CallOffChain();
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return "not ready yet";
            if (orders.Count == 1) return Run(orders[0], fromId);
            string first = Start(orders[0], fromId, out bool ok);
            if (!ok) return Sentence(first) + " The rest called off.";
            var said = new List<string> { first };
            int k = 1;
            // (what is over at once is done at once)
            while (k < orders.Count && stepDone == null)
            {
                said.Add(Start(orders[k], fromId, out ok));
                k++;
                if (!ok) return string.Join(" ", said.Select(Sentence)) + (k < orders.Count ? " The rest called off." : "");
            }
            for (; k < orders.Count; k++) pending.Add((orders[k].kind, orders[k].arg, fromId));
            string then = pending.Count > 0 ? $" Then: {string.Join(", then ", pending.Select(x => Words((x.kind, x.arg))))}." : "";
            return string.Join(" ", said.Select(Sentence)) + then;
        }

        /// <summary>Carry out one order of a chain: its answer, whether it was taken, and (in
        /// `stepDone`) how to tell it is over; none for one over at once.</summary>
        string Start((string kind, string arg) o, int fromId, out bool ok)
        {
            int before = Orders;
            int sales0 = Pilot != null ? Pilot.Sales : 0;
            string answer = Run(o, fromId);
            stepDone = null;
            stepWords = Words(o);
            ok = true;
            if (Instant(o.kind) || Standing(o.kind) || Pilot == null) return answer;
            // (an order refused: "don't know a place called ...", "no ore aboard")
            ok = Orders > before;
            if (!ok) return answer;
            var pilot = Pilot;
            var prey = pilot.Target;
            bool PilotGone() => pilot == null || pilot != Pilot;
            switch (o.kind)
            {
                // (over on arriving; it holds after a warp it couldn't make, too: that one failed)
                case "goto": stepDone = () => PilotGone() ? -1 : pilot.Current == Wingman.Order.Goto ? 0 : pilot.Doing.StartsWith("arrived") ? 1 : -1; break;
                case "dock": stepDone = () => PilotGone() ? -1 : pilot.Current == Wingman.Order.Dock ? 0 : Docking.Docked ? 1 : -1; break;
                // (over with the ship it went for destroyed, whatever it goes for next)
                case "attack": stepDone = () => PilotGone() ? -1 : prey == null || (prey.TryGetComponent(out Health ph) && ph.Dead) ? 1 : pilot.Current == Wingman.Order.Attack ? 0 : -1; break;
                // (mining: over with its first load sold; selling: with the sale; no longer at it before then: it couldn't)
                case "mine": stepDone = () => PilotGone() ? -1 : pilot.Sales > 0 ? 1 : pilot.Current == Wingman.Order.Mine ? 0 : -1; break;
                case "sell": stepDone = () => PilotGone() ? -1 : pilot.Sales > sales0 ? 1 : pilot.Current == Wingman.Order.Mine ? 0 : -1; break;
            }
            return answer;
        }

        /// <summary>The next order of a chain, once the one before it is over (Update).</summary>
        void NextInChain()
        {
            if (pending.Count == 0 || Pilot == null) return;
            int how = stepDone != null ? stepDone() : 1;
            if (how == 0) return;
            if (how < 0)
            {
                // (the step couldn't be done: what was to follow it isn't done either)
                var left = pending.Select(x => Words((x.kind, x.arg))).ToList();
                CallOffChain();
                if (CoopClient.Active) CoopChat.Say($"{Me}: couldn't {stepWords}, so not {string.Join(" or ", left)}.");
                return;
            }
            var n = pending[0];
            pending.RemoveAt(0);
            string answer = Start((n.kind, n.arg), n.from, out bool ok);
            if (!ok && pending.Count > 0) { pending.Clear(); answer = Sentence(answer) + " The rest called off."; }
            if (CoopClient.Active && !string.IsNullOrEmpty(answer)) CoopChat.Say($"{Me}: {answer}");
        }

        static string Sentence(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1) + (".!?".IndexOf(s[s.Length - 1]) >= 0 ? "" : ".");

        static string Words((string kind, string arg) o)
        {
            switch (o.kind)
            {
                case "goto": return $"warp to {o.arg}";
                case "attack": return o.arg.Length > 0 ? $"attack {o.arg}" : "attack";
                case "mine": return o.arg.Length > 0 ? $"mine at {o.arg}" : "mine";
                case "follow": return o.arg.Length > 0 ? $"follow {o.arg}" : "follow";
                case "give": return "give credits";
                default: return o.kind;
            }
        }

        const RegexOptions RxOpt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex GiveRx = new Regex(@"^(?:please\s+)?(?<verb>give|transfer|send|pay|hand\s+over|hand|wire)\b(?<rest>.*)$", RxOpt);
        static readonly Regex MoneyRx = new Regex(@"\b(?:credits?|cr|money|cash)\b", RxOpt);
        // ("pay me", "pay us 300", "pay up": not "pay attention")
        static readonly Regex PayRx = new Regex(@"^(?:me|us|up)\b", RxOpt);
        static readonly Regex ToRx = new Regex(@"\bto\s+(?<to>[^,.!?]+?)\s*$", RxOpt);
        static readonly Regex AmountRx = new Regex(@"(?<n>\d[\d,]*(?:\.\d+)?)\s*(?<k>k\b|thousand\b)?", RxOpt);
        static readonly Regex HalfRx = new Regex(@"\bhalf\b", RxOpt);
        static readonly Regex AllRx = new Regex(@"\b(?:all|everything|every|your)\b", RxOpt);
        // ("give Ann 500 credits": a name first, then the amount)
        static readonly Regex NameFirstRx = new Regex(@"^(?<who>[a-z][\w\-]*)\s+(?=\d|all\b|half\b|your\b|the\b|some\b|credits?\b|cr\b)", RxOpt);
        static readonly string[] NotNames = { "me", "us", "them", "him", "her", "your", "the", "my", "our", "all", "half", "some", "a", "every", "everything" };

        /// <summary>An order to hand over credits ("give me 500 credits", "transfer your credits to
        /// Ann", "give Ann 2k credits", "pay me 300"), as the "give" order's target:
        /// "&lt;amount&gt;[ to &lt;name&gt;]", the amount a number, "all", "half", or "?" when it
        /// doesn't say how many ("give me some credits"). Null if it isn't one.</summary>
        public static string GiveArgs(string words)
        {
            var m = GiveRx.Match((words ?? "").Trim());
            if (!m.Success) return null;
            string rest = m.Groups["rest"].Value.Trim();
            bool pay = m.Groups["verb"].Value.Equals("pay", StringComparison.OrdinalIgnoreCase);
            if (pay ? !PayRx.IsMatch(rest) && !MoneyRx.IsMatch(rest) : !MoneyRx.IsMatch(rest)) return null;
            string to = "";
            var tm = ToRx.Match(rest);
            if (tm.Success)
            {
                to = tm.Groups["to"].Value.Trim();
                rest = rest.Substring(0, tm.Index).Trim();
            }
            else
            {
                var nf = NameFirstRx.Match(rest);
                if (nf.Success && Array.IndexOf(NotNames, nf.Groups["who"].Value.ToLowerInvariant()) < 0)
                {
                    to = nf.Groups["who"].Value;
                    rest = rest.Substring(nf.Length).Trim();
                }
            }
            if (Array.IndexOf(NotNames, to.ToLowerInvariant()) >= 0) to = "";
            string amount = "?";
            var nm = AmountRx.Match(rest);
            if (nm.Success && double.TryParse(nm.Groups["n"].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                amount = Math.Floor(nm.Groups["k"].Success ? n * 1000.0 : n).ToString(CultureInfo.InvariantCulture);
            else if (HalfRx.IsMatch(rest)) amount = "half";
            else if (AllRx.IsMatch(rest)) amount = "all";
            return to.Length > 0 ? $"{amount} to {to}" : amount;
        }

        /// <summary>What a hold has aboard, in a line: how full, the cargo (largest first), the credits.</summary>
        public static string CargoLine(CargoHold hold, int most = 5)
        {
            if (hold == null) return "no cargo hold.";
            var table = IndustryTable.Get();
            // (the most first: by weight, parts after the rest)
            var items = hold.Contents().Where(e => e.amount > 0.005f)
                            .OrderBy(e => table.Kind(e.id) == ItemKind.Part ? 1 : 0).ThenByDescending(e => e.amount).ToList();
            string T(float t) => t >= 9.95f ? t.ToString("F0", CultureInfo.InvariantCulture) : Mathf.Max(0.1f, t).ToString("0.#", CultureInfo.InvariantCulture);
            var parts = new List<string>();
            foreach (var e in items.Take(most))
                parts.Add(table.Kind(e.id) == ItemKind.Part ? $"{e.amount:F0} x {table.Name(e.id)}" : $"{T(e.amount)} t {table.Name(e.id)}");
            if (items.Count > most)
            {
                var rest = items.Skip(most).ToList();
                float restT = rest.Where(e => table.Kind(e.id) != ItemKind.Part).Sum(e => e.amount);
                parts.Add(restT > 0.05f ? $"{rest.Count} more ({T(restT)} t)" : $"{rest.Count} more");
            }
            string what = parts.Count > 0 ? ": " + string.Join(", ", parts) : ", empty";
            return $"hold {hold.Used:F0} of {hold.capacity:F0} t ({hold.Fraction:P0} full){what}; {hold.Credits:N0} cr aboard.";
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
                case "mine":
                {
                    // (at a field by name, or the nearest; "asteroids" is any field)
                    WarpPoint field = null;
                    // ("asteroids in the Outer Drift", "at the Inner Belt": the field's name, if any;
                    // "rocks", "some asteroids", "the nearest field": the nearest)
                    string name = MineFieldName(target);
                    if (name.Length > 0)
                    {
                        field = Place(name);
                        if (field == null || field.kind != WarpPoint.Kind.AsteroidField) return $"don't know an asteroid field called {name}.";
                    }
                    Pilot.Mine(field);
                    lastLeader = 0;
                    Orders++;
                    return $"mining {(field != null ? "in " + field.displayName : "the nearest asteroid field")}; I'll sell the ore when the hold is full.";
                }
                case "sell":
                {
                    var hold = cc.GetComponentInChildren<CargoHold>();
                    if (hold == null || hold.OreTonnes < 0.5f) return "no ore aboard to sell.";
                    Pilot.SellNow();
                    lastLeader = 0;
                    Orders++;
                    return $"taking {hold.OreTonnes:F0} t of ore to the nearest market.";
                }
                case "cargo":
                    return CargoLine(cc.GetComponentInChildren<CargoHold>());
                case "give":
                {
                    var hold = cc.GetComponentInChildren<CargoHold>();
                    if (hold == null || hold.Credits < 1f) return "I've no credits to give.";
                    var c = CoopClient.Instance;
                    if (c == null || !CoopClient.Active) return "not connected to anyone to give credits to.";
                    // "<amount>[ to <name>]": all, half or a number
                    string amountWord = target, name = "";
                    int at = target.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
                    if (at >= 0) { amountWord = target.Substring(0, at).Trim(); name = target.Substring(at + 4).Trim(); }
                    else if (target.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) { amountWord = ""; name = target.Substring(3).Trim(); }
                    CoopPlayer who;
                    if (name.Length > 0 && !name.Equals("me", StringComparison.OrdinalIgnoreCase))
                    {
                        who = c.Players.FirstOrDefault(p => p != null && p.id != c.MyId && p.name.ToLowerInvariant().Contains(name.ToLowerInvariant()));
                        if (who == null) return $"there's no player called {name}.";
                    }
                    else who = c.Players.FirstOrDefault(p => p != null && p.id != c.MyId && !IsBot(p) && p.id == (fromId != 0 ? fromId : lastSpeaker))
                               ?? NearestHuman(cc.transform.position);
                    if (who == null) return "there's nobody to give credits to.";
                    if (IsBot(who)) return $"{who.name} is a bot: credits go to players.";
                    var mine = c.Players.FirstOrDefault(p => p != null && p.id == c.MyId);
                    if (mine != null && who.level != mine.level) return $"{who.name} isn't in this part of space: credits go to players here.";
                    float amount;
                    string aw = amountWord.ToLowerInvariant();
                    if (aw == "?") return $"how many credits? I've {hold.Credits:N0} cr (\"give me 500 credits\", or \"give me all your credits\").";
                    if (aw == "" || aw == "all" || aw == "everything") amount = hold.Credits;
                    else if (aw == "half") amount = hold.Credits / 2f;
                    else if (!float.TryParse(aw.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out amount) || !(amount >= 1f))
                        return "how many credits? (\"give 500 credits\", or \"give all credits\")";
                    amount = Mathf.Floor(amount);
                    if (amount < 1f) return "I've not a whole credit to give.";
                    if (amount > hold.Credits + 1e-3f) return $"I've only {hold.Credits:N0} cr.";
                    if (!CoopGifts.Give(hold, who.id, amount)) return "couldn't send the credits.";
                    Orders++;
                    return $"transferred {amount:N0} cr to {who.name}; {hold.Credits:N0} cr left.";
                }
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
            var hold = cc.GetComponentInChildren<CargoHold>();
            string cargo = hold != null ? $", hold {hold.Used:F0}/{hold.capacity:F0} t, {hold.Credits:N0} cr" : "";
            return $"{cc.shipClass} {cc.shipName}, hull {Fraction(cc.GetComponent<Health>()):P0}, shield {(sh != null ? sh.Strength / Mathf.Max(1f, sh.capacity) : 0f):P0}" +
                   $"{missiles}{cargo}; {Pilot.Doing}{(near != null ? $", near {near.displayName}" : "")}.";
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
        [Serializable] class ChatModeBody { public string mode; }

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
                    {
                        int.TryParse(ctx.Request.QueryString["after"], out int after);
                        // (listener=1: the reader answers talk; wait=N: hold the answer up to N s for a new message)
                        bool listening = ctx.Request.QueryString["listener"] == "1";
                        if (listening) listenerSeen = Time.realtimeSinceStartup;
                        float.TryParse(ctx.Request.QueryString["wait"], NumberStyles.Float, CultureInfo.InvariantCulture, out float wait);
                        if (wait > 0f && CoopChat.LastSeq <= after && waiting.Count < 16)
                        {
                            float until = Time.realtimeSinceStartup + Mathf.Min(wait, 60f);
                            if (listening) listenerSeen = until;
                            waiting.Add((ctx, after, until));
                            return;
                        }
                        json = MessagesJson(after);
                        break;
                    }
                    case "/cargo" when !post: json = CargoJson(); break;
                    case "/destinations" when !post: json = DestinationsJson(); break;
                    case "/chatmode" when !post: json = ChatModeJson(); break;
                    case "/chatmode" when post:
                    {
                        // {"mode": llm|personality|paused, or on|off|pause|resume|unpause}
                        var b = Parse<ChatModeBody>(body);
                        var was = Chat;
                        if (b == null || !SetChatMode(b.mode))
                        {
                            code = 400;
                            json = "{\"ok\":false,\"error\":" + Q($"mode '{b?.mode}': one of llm, personality, paused (or on, off, pause, resume)") + "}";
                            break;
                        }
                        if (Chat != was && CoopClient.Active) CoopChat.Say($"{Me}: {ChatModeLine()}");
                        json = ChatModeJson($"\"ok\":true,\"previous\":{Q(ModeName(was))},");
                        break;
                    }
                    case "/say" when post:
                    {
                        var b = Parse<SayBody>(body);
                        bool ok = b != null && CoopChat.Say(b.text);
                        // (longer than a message may be: what was said was cut, and the caller is told)
                        bool cut = ok && CoopChat.Clean(b.text, int.MaxValue).Length > CoopChat.MaxLength;
                        json = ok ? (cut ? $"{{\"ok\":true,\"cut\":true,\"max\":{CoopChat.MaxLength}}}" : "{\"ok\":true}") : "{\"ok\":false,\"error\":" + Q(CoopClient.Active ? "nothing to say" : "not connected to a co-op game") + "}";
                        break;
                    }
                    case "/order" when post:
                    {
                        var b = Parse<OrderBody>(body);
                        if (!Instant((b?.order ?? "").Trim().ToLowerInvariant())) CallOffChain();
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
                    case "/cargo":
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
            Respond(ctx, code, json);
        }

        static void Respond(HttpListenerContext ctx, int code, string json)
        {
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
            sb.Append($",\"chat_mode\":{Q(ModeName(Chat))},\"listener_seen_s\":{F(ListenerSeenSeconds)}");
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
                var hold = cc.GetComponentInChildren<CargoHold>();
                if (hold != null) sb.Append($",\"hold_t\":{F(hold.Used)},\"ore_t\":{F(hold.OreTonnes)},\"capacity_t\":{F(hold.capacity)},\"credits\":{F(hold.Credits)}");
                if (near != null) sb.Append($",\"near\":{Q(near.displayName)},\"near_km\":{F(Vector3.Distance(cc.transform.position, near.Position) / 1000f)}");
                sb.Append("}");
                sb.Append($",\"order\":{{\"order\":{Q(Pilot.Current.ToString().ToLowerInvariant())},\"leader\":{Q(Pilot.LeaderName)}");
                sb.Append($",\"target\":{Q(Wingman.NameOf(Pilot.Target))},\"destination\":{Q(Pilot.Destination != null ? Pilot.Destination.displayName : "")},\"doing\":{Q(Pilot.Doing)}" +
                          (Pilot.Current == Wingman.Order.Mine ? $",\"step\":{Q(Pilot.Step.ToString().ToLowerInvariant())},\"sold_t\":{F(Pilot.TonnesSold)},\"earned\":{F(Pilot.CreditsEarned)},\"sales\":{Pilot.Sales}" : "") + "}");
                // what is near, enemies first
                sb.Append(",\"nearby\":[");
                bool first = true;
                Vector3 at = cc.transform.position;
                foreach (var t in Targets.All.Where(t => t != null && t.gameObject.activeInHierarchy && !Sensors.Unseen(t) && (t.position - at).sqrMagnitude < 50000f * 50000f)
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
            var sb = new StringBuilder($"{{\"last\":{CoopChat.LastSeq},\"chat_mode\":{Q(ModeName(Chat))},\"messages\":[");
            bool first = true;
            foreach (var l in CoopChat.Lines)
            {
                if (l.seq <= after) continue;
                if (!first) sb.Append(',');
                first = false;
                bool mine = c != null && l.from == c.MyId;
                bool toMe = !mine && AddressedPart(l.text, Me) != null;
                // (handled: an order the bot carried out itself; the listener doesn't order it again)
                sb.Append($"{{\"seq\":{l.seq},\"from\":{Q(l.name)},\"mine\":{B(mine)},\"to_me\":{B(toMe)},\"handled\":{B(handled.Contains(l.seq))},\"text\":{Q(l.text)}}}");
            }
            return sb.Append("]}").ToString();
        }

        /// <summary>The chat mode (and how long since a listener read the channel), with `extra` fields first.</summary>
        string ChatModeJson(string extra = "") =>
            $"{{{extra}\"mode\":{Q(ModeName(Chat))},\"listener_seen_s\":{F(ListenerSeenSeconds)},\"listening\":{B(Listened)},\"resumes_to\":{Q(ModeName(Chat == ChatMode.Paused ? beforePause : Chat))}}}";

        string CargoJson()
        {
            var cc = Pilot != null && Pilot.demo != null ? Pilot.demo.carrier : null;
            var hold = cc != null ? cc.GetComponentInChildren<CargoHold>() : null;
            if (hold == null) return "{\"error\":\"no ship or no hold yet\"}";
            var table = IndustryTable.Get();
            var sb = new StringBuilder($"{{\"capacity_t\":{F(hold.capacity)},\"used_t\":{F(hold.Used)},\"free_t\":{F(hold.Free)},\"full\":{F(hold.Fraction)},\"ore_t\":{F(hold.OreTonnes)},\"credits\":{F(hold.Credits)},\"items\":[");
            bool first = true;
            foreach (var e in hold.Contents())
            {
                if (e.amount <= 0.005f) continue;
                if (!first) sb.Append(',');
                first = false;
                var kind = table.Kind(e.id);
                sb.Append($"{{\"id\":{Q(e.id)},\"name\":{Q(table.Name(e.id))},\"kind\":{Q(kind.ToString().ToLowerInvariant())},\"amount\":{F(e.amount)},\"unit\":{Q(kind == ItemKind.Part ? "parts" : "t")}}}");
            }
            return sb.Append($"],\"line\":{Q(CargoLine(hold))}}}").ToString();
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
                sb.Append($"{{\"name\":{Q(w.displayName)},\"kind\":{Q(string.IsNullOrEmpty(w.kindLabel) ? w.kind.ToString() : w.kindLabel)},\"hostile\":{B(w.Threatened)},\"km\":{F(Vector3.Distance(w.Position, at) / 1000f)}}}");
            }
            return sb.Append("]}").ToString();
        }
    }
}
