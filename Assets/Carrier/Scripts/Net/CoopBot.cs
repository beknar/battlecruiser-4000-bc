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
    /// "Wingman, mine asteroids" (and sell the ore when the hold is full; "mine platinum", "mine
    /// the most valuable ore", "mine gold at the Inner Belt" choose the rocks), "Wingman, sell",
    /// "Wingman status" ("all ships", "everyone" or "wingmen" orders every bot), "Wingman, attack
    /// my target" (what the speaker's game says they have locked), "Wingman, defend me" (to them
    /// from anywhere, and fight what's on them; with only this wingman in the room, "I'm under
    /// attack" or "help me" needs no name), "Wingman, where are you", or a target's name alone
    /// ("Wingman, Fang Hunter"). It answers, and says when it starts a fight, finishes one,
    /// arrives or can't do what it was told. "Wingman, your name is Ace" (or "call yourself Ace",
    /// "rename to Ace"), from its leader or the room's host, renames it on the server: it answers
    /// in its new name, and keeps it for its next start (a kept name wins over -name). "Wingman,
    /// give me your ore" (the platinum, 100 t of iron) hands ore from its hold to the speaker's,
    /// and "Wingman, take my ore" is sent with the ore by the speaker's own game (CoopCargo): it
    /// says what came, and what there was no room for. "Wingman, salvage" (scavenge, salvage the
    /// wrecks, salvage the wreck of the Fang Hunter) strips the wrecks near it, one after another,
    /// sells what it takes when the hold is full and goes back to its order before when none is
    /// left; "Wingman, give me the parts" hands over the parts aboard, and keeps those it
    /// salvages for the speaker (handed over when they're near) rather than selling them.
    ///
    /// It also answers on a small HTTP API on this machine only (127.0.0.1, -botport, 7781 by
    /// default), for an AI to fly it (Tools/CoopAI/coop_ai_mcp.py makes it an MCP server):
    ///   GET  /status            its ship, its order, what is near it, the players
    ///   GET  /messages?after=N  the comms channel after message N
    ///   GET  /destinations      the places to warp to, nearest first
    ///   POST /say   {text}      say something on the comms channel
    ///   POST /order {order, target}  follow|hold|attack|goto|regroup|dock|mine|sell|defend|assist|rename|giveore|salvage
    ///                           (defend and assist: a player's name, or the last to speak;
    ///                           rename: the new name, answered on the comms; giveore:
    ///                           "&lt;N|all|half&gt; &lt;ore|mineral|parts&gt;[ to &lt;name&gt;]";
    ///                           salvage: what the wreck was, or nothing for the nearest)
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
        int saidBackToMining, saidBackToSalvaging;
        Wingman backToMiningOf;

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
            string kept = KeptName;
            if (kept != null) Debug.Log($"[CoopBot] called {kept} (kept from its last run; launched as {LaunchName})");
            CoopChat.Received += Heard;
            CoopClient.Message += OnMessage;
            CoopClient.Renamed += OnRenamed;
            CoopCargo.Arrived += OnCargoArrived;
            CoopCargo.Returned += OnCargoReturned;
            StartApi();
        }

        void OnDestroy()
        {
            CoopChat.Received -= Heard;
            CoopClient.Message -= OnMessage;
            CoopClient.Renamed -= OnRenamed;
            CoopCargo.Arrived -= OnCargoArrived;
            CoopCargo.Returned -= OnCargoReturned;
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
                    Pilot.Sold += what => { if (CoopClient.Active) CoopChat.Say($"{Me}: {what}.", Via.Event); };
                    Pilot.Remark += what => { if (CoopClient.Active) CoopChat.Say($"{Me}: {what}.", Via.Event); };
                    if (lastLeader != 0) Pilot.Follow(lastLeader);
                }
            }
            if (!greeted && CoopClient.Active && Pilot != null)
            {
                greeted = true;
                var human = Humans().FirstOrDefault();
                if (human != null) { CallOffChain(); Pilot.Follow(human.id); lastLeader = lastSpeaker = human.id; }
                CoopChat.Say($"{Me} on station{(human != null ? $", forming up on {human.name}" : "")}. Orders by name: \"{Me}, follow me / defend me / hold / attack [my target] / warp to <place> / dock / mine / sell / salvage / cargo / give credits / give me your ore / take my ore / where are you / status\".", Via.Event);
            }
            if (!CoopClient.Active) greeted = false;
            // says what it starts doing, when it's worth saying
            if (Pilot != null && CoopClient.Active && Pilot.Doing != lastDoing)
            {
                string now = Pilot.Doing;
                bool worth = now.StartsWith("target destroyed") || now.StartsWith("arrived") || now.StartsWith("can't");
                lastDoing = now;
                if (worth) CoopChat.Say($"{Me}: {now}.", Via.Event);
            }
            // waiting over a world for a landed leader: said once a landing (not again after each
            // fight while it waits)
            var over = Pilot != null ? Pilot.WaitingOver : null;
            if (over != lastWaitingOver)
            {
                lastWaitingOver = over;
                if (over != null && CoopClient.Active) CoopChat.Say($"{Me}: {Pilot.WaitingLine}.", Via.Event);
            }
            // a fight starting is said once, whatever it says it is doing on the way in
            bool fighting = Pilot != null && Pilot.Fighting;
            if (fighting && !wasFighting && Pilot.Target != null)
            {
                wasFighting = true;
                var info = Pilot.Target.GetComponent<TargetInfo>();
                if (CoopClient.Active) CoopChat.Say($"{Me}: engaging {(info != null && !string.IsNullOrEmpty(info.Name) ? info.Name : Pilot.Target.name)}.", Via.Event);
            }
            else if (!fighting && wasFighting) wasFighting = false;
            // (a fight over while mining, back at the rocks: said, so the order isn't lost)
            if (Pilot != backToMiningOf)
            {
                backToMiningOf = Pilot;
                saidBackToMining = Pilot != null ? Pilot.BackToMining : 0;
                saidBackToSalvaging = Pilot != null ? Pilot.BackToSalvaging : 0;
            }
            if (Pilot != null && Pilot.BackToMining != saidBackToMining)
            {
                saidBackToMining = Pilot.BackToMining;
                if (CoopClient.Active) CoopChat.Say($"{Me}: {WingmanReplies.Get("back_to_mining")}", Via.Event);
            }
            // (and while salvaging, back at the wrecks)
            if (Pilot != null && Pilot.BackToSalvaging != saidBackToSalvaging)
            {
                saidBackToSalvaging = Pilot.BackToSalvaging;
                if (CoopClient.Active) CoopChat.Say($"{Me}: back to salvaging.", Via.Event);
            }
            HandOverParts();
            SayDoingIfChanged();
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
            // (someone new in the room: what it's doing, said again at once)
            if (m.t == CoopWire.Join) saidDoingAt = float.NegativeInfinity;
            if (m.t != CoopWire.Join || m.p == null || IsBot(m.p) || Pilot == null) return;
            if (Pilot.LeaderId != 0 || Pilot.Current != Wingman.Order.Hold || Pilot.Released) return;
            CallOffChain();
            Pilot.Follow(m.p.id);
            lastLeader = m.p.id;
            CoopChat.Say($"{Me}: welcome, {m.p.name}. Forming up on you; orders by name (\"{Me}, help\").{(CoopClient.Instance.ChatLogged ? " Comms are logged on this server." : "")}", Via.Event);
        }

        // ------------------------------------------------------------ orders on the comms channel

        void Heard(CoopChat.Line line)
        {
            var c = CoopClient.Instance;
            if (c == null || line.from == c.MyId) return;
            lastSpeaker = line.from;
            string rest = AddressedPart(line.text, Me) ?? UnnamedCall(line);
            if (rest == null) return;
            string reply = Take(rest, line.from, line.seq);
            // (a new name asked for: it answers when the server does, in its new name; cargo to
            // take: when it comes, from the speaker's game)
            if (reply == RenameAsked || reply == CargoComing) return;
            if (!string.IsNullOrEmpty(reply)) { CoopChat.Say($"{Me}: {reply}", Via.Order); return; }
            // talk, not an order: by the chat mode
            float now = Time.realtimeSinceStartup;
            switch (Chat)
            {
                case ChatMode.Paused:
                    // (standing by: said at most once in half a minute)
                    if (now - toldPaused > 30f)
                    {
                        toldPaused = now;
                        CoopChat.Say($"{Me}: {Personality?.PausedLine(Facts(line.from)) ?? "comms quiet, standing by. Orders still work."}", Via.Paused);
                    }
                    return;
                case ChatMode.Llm when Listened:
                    return;        // (the listener answers it)
            }
            string said = Personality?.Reply(line.name, rest, now, Facts(line.from));
            if (!string.IsNullOrEmpty(said)) CoopChat.Say($"{Me}: {said}", Via.Personality);
            // (no personality to talk with: say so, not more than once in half a minute)
            else if (now - toldOrdersOnly > 30f)
            {
                toldOrdersOnly = now;
                CoopChat.Say($"{Me}: {WingmanReplies.Get("orders_only")}", Via.Fixed);
            }
        }

        /// <summary>A call for help to no one by name ("I'm under attack", "help me", "defend
        /// me") from a player, while this is the only wingman in the room: its words, taken as an
        /// order to it; null for anything else (a plain "help" among them: that's to the others).</summary>
        string UnnamedCall(CoopChat.Line line)
        {
            var c = CoopClient.Instance;
            if (c == null || line.from == c.MyId) return null;
            var p = ParseOrder(line.text);
            if (p == null || p.Value.kind != "defend" || p.Value.arg.Length > 0) return null;
            var who = c.Players.FirstOrDefault(x => x != null && x.id == line.from);
            if (who == null || IsBot(who)) return null;
            return c.Players.Any(x => x != null && x.id != c.MyId && IsBot(x)) ? null : line.text;
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

        /// <summary>The name it was launched with (-name), whatever it is called now: what its
        /// settings are kept under.</summary>
        public static string LaunchName => LaunchArgs.ValueOf("-name") ?? "Wingman";

        // (kept between runs, per bot name, in this machine's player prefs: the bot's HOME on a server)
        static string ChatPrefKey => ChatPref + LaunchName;

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
                    return WingmanReplies.Get("chat_personality");
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

        /// <summary>Who may switch the chat mode or rename it (MayChangeChat), as said: "Ann or Bob".</summary>
        string Deciders()
        {
            var c = CoopClient.Instance;
            var who = new List<string>();
            if (Pilot != null && Pilot.LeaderId != 0) who.Add(Pilot.LeaderName);
            if (c != null && c.HostId != 0 && c.HostId != c.MyId) who.Add(c.NameOf(c.HostId));
            return string.Join(" or ", who.Distinct());
        }

        string ChatOrder(string arg, int fromId)
        {
            if (arg.Length == 0) return ChatModeLine();
            if (!MayChangeChat(fromId)) return $"chat mode stays {ModeName(Chat)}: only {Deciders()} can switch it.";
            var was = Chat;
            SetChatMode(arg);
            return Chat == was ? $"chat mode already {ModeName(Chat)}." : ChatModeLine();
        }

        // ------------------------------------------------------------ its name

        const string NamePref = "CoopBot.Name.";
        // (kept under the name it was launched with, as the chat mode is)
        static string NamePrefKey => NamePref + LaunchName;

        /// <summary>The name it was last given ("your name is Ace"), kept for its next start, or
        /// null. It wins over -name (the MCP server always launches it with one).</summary>
        public static string KeptName
        {
            get
            {
                try
                {
                    string s = PlayerPrefs.GetString(NamePrefKey, "").Trim();
                    return s.Length > 0 ? s : null;
                }
                catch (Exception) { return null; }
            }
        }

        static void KeepName(string name)
        {
            if (!Application.isPlaying) return;
            try
            {
                PlayerPrefs.SetString(NamePrefKey, name ?? "");
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        /// <summary>What a rename order answers at once when the server has been asked: nothing
        /// yet (it answers when the server does, in its new name: OnRenamed).</summary>
        public const string RenameAsked = "(a new name asked for)";
        // (a rename the bot asked for, not answered yet)
        bool renameAsked;

        /// <summary>A name as said in an order: without quotes round it or a stop after it.</summary>
        public static string NameIn(string said) =>
            (said ?? "").Trim().TrimEnd('.', '!', '?', ',', ';').Trim().Trim('"', '\'', '“', '”', '‘', '’').Trim();

        /// <summary>"your name is Ace": asks the server for the name. Its leader or the room's host
        /// may (MayChangeChat, as with the chat mode); the API always may (fromId 0). The answer
        /// now: RenameAsked, or why not.</summary>
        string Rename(string said, int fromId)
        {
            string name = NameIn(said);
            if (fromId != 0 && !MayChangeChat(fromId)) return $"my name stays {Me}: only {Deciders()} can change it.";
            if (name.Length == 0) return "rename to what?";
            string why = CoopWire.NameRefused(name);
            if (why.Length > 0) return $"can't take the name {name}: {why}.";
            if (name == Me) return $"I'm {Me} already.";
            var c = CoopClient.Instance;
            if (c == null || !CoopClient.Active) return "not connected to a co-op server.";
            if (c.Players.Any(p => p != null && p.id != c.MyId && string.Equals((p.name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)))
                return $"can't take the name {name}: another player is called {name}.";
            if (!c.Rename(name)) return "not connected to a co-op server.";
            renameAsked = true;
            Orders++;
            return RenameAsked;
        }

        /// <summary>The server's answer to its new name: said in the name it has now, and a new one kept.</summary>
        void OnRenamed(bool ok, string name, string why)
        {
            if (!renameAsked) return;
            renameAsked = false;
            if (ok) KeepName(name);
            if (!CoopClient.Active) return;
            if (ok) CoopChat.Say($"{Me}: {Me} it is. Orders by my new name: \"{Me}, help\".", Via.Order);
            else CoopChat.Say($"{Me}: can't take that name: {why}.", Via.Order);
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
            if (Pilot.Fighting && Pilot.Target != null) f["target"] = Wingman.NameOf(Pilot.Target);
            f["docked"] = Docking.Docked ? "docked" : "";
            return f;
        }

        string saidDoing = "";
        float saidDoingAt = float.NegativeInfinity, nextDoingLook;

        /// <summary>What it's doing, for the players' HUD: when it changes (at most every 2 s), and
        /// every 15 s so a game that joins later has it.</summary>
        void SayDoingIfChanged()
        {
            if (!CoopClient.Active) { saidDoing = ""; return; }
            float now = Time.realtimeSinceStartup;
            if (now < nextDoingLook) return;
            nextDoingLook = now + 0.5f;
            string line = DoingLine();
            // (nothing to say any more, a ship lost say: the line taken down, once)
            if (line.Length == 0)
            {
                if (saidDoing.Length > 0) { CoopChat.SayDoing(""); saidDoing = ""; saidDoingAt = now; }
                return;
            }
            bool changed = line != saidDoing;
            if ((changed && now - saidDoingAt >= 2f) || now - saidDoingAt >= 15f)
            {
                CoopChat.SayDoing(line);
                saidDoing = line;
                saidDoingAt = now;
            }
        }

        /// <summary>What it's doing, in a few words ("mining in Outer Drift: 34 t of ore aboard").</summary>
        public string DoingLine()
        {
            var pl = Pilot;
            if (pl == null) return "";
            if (pl.Fighting) return pl.Target != null ? $"fighting {Wingman.NameOf(pl.Target)}" : "fighting";
            switch (pl.Current)
            {
                case Wingman.Order.Mine:
                {
                    var hold = pl.demo != null && pl.demo.carrier != null ? pl.demo.carrier.GetComponentInChildren<CargoHold>() : null;
                    return MineLine(pl.Step, pl.MineField != null ? pl.MineField.displayName : null,
                                    pl.Market != null ? pl.Market.displayName : null, hold != null ? hold.OreTonnes : -1f, pl.MineChoice, pl.OreMissing);
                }
                case Wingman.Order.Salvage:
                {
                    var hold = pl.demo != null && pl.demo.carrier != null ? pl.demo.carrier.GetComponentInChildren<CargoHold>() : null;
                    return SalvageLine(pl.SalvagingStep, pl.SalvageTarget != null ? pl.SalvageTarget.Of : null,
                                       pl.Market != null ? pl.Market.displayName : null, hold != null ? hold.Used : -1f);
                }
                case Wingman.Order.Follow: return pl.LeaderName.Length > 0 ? $"following {pl.LeaderName}" : "following";
                case Wingman.Order.Defend: return pl.LeaderName.Length > 0 ? $"defending {pl.LeaderName}" : "defending";
                case Wingman.Order.Attack: return "attacking";
                case Wingman.Order.Goto: return pl.Destination != null ? $"heading for {pl.Destination.displayName}" : "on the way";
                case Wingman.Order.Dock: return "docking";
                default: return Docking.Docked ? "docked" : "holding position";
            }
        }

        /// <summary>The mine order's line: where it mines (or sells), for what ("mining for
        /// platinum in the Inner Belt"; with none of it there, "mining the most valuable ore in the
        /// Inner Belt (no platinum here)"), and the ore aboard (-1: not known).</summary>
        public static string MineLine(Wingman.MineStep step, string field, string market, float tonnes,
                                      Wingman.OreChoice choice = default, bool missing = false)
        {
            bool nearest = string.IsNullOrEmpty(field);
            string what = choice.kind == Wingman.OreChoice.Kind.Nearest ? ""
                        : choice.kind == Wingman.OreChoice.Kind.Mineral && !missing ? $" for {choice.MineralName}" : " the most valuable ore";
            string lacking = choice.kind == Wingman.OreChoice.Kind.Mineral && missing ? $" (no {choice.MineralName} here)" : "";
            string where = step == Wingman.MineStep.Leave
                ? $"heading back to {(nearest ? "the nearest asteroid field" : field)}{(choice.kind == Wingman.OreChoice.Kind.Mineral ? $" for {choice.MineralName}" : "")}"
                : step >= Wingman.MineStep.ToMarket
                ? $"selling ore at {(string.IsNullOrEmpty(market) ? "the nearest market" : market)}"
                : what.Length == 0
                ? $"mining {(nearest ? "the nearest asteroid field" : "in " + field)}"
                : $"mining{what} {(nearest ? "at the nearest asteroid field" : "in " + field)}{lacking}";
            return tonnes >= 0f ? $"{where}: {tonnes:F0} t of ore aboard" : where;
        }

        /// <summary>The salvage order's line: the wreck it is at or flying to ("salvaging the wreck
        /// of Pirate Cruiser"; none chosen yet, "looking for wrecks to salvage"), or the market it
        /// sells at, and the tonnes aboard (-1: not known).</summary>
        public static string SalvageLine(Wingman.SalvageStep step, string wreckOf, string market, float tonnes)
        {
            string what = step == Wingman.SalvageStep.Leave ? "heading back to the wrecks"
                        : step >= Wingman.SalvageStep.ToMarket ? $"selling salvage at {(string.IsNullOrEmpty(market) ? "the nearest market" : market)}"
                        : string.IsNullOrEmpty(wreckOf) ? "looking for wrecks to salvage"
                        : step == Wingman.SalvageStep.Strip ? $"salvaging the wreck of {wreckOf}"
                        : $"on the way to the wreck of {wreckOf}";
            return tonnes >= 0f ? $"{what}: {tonnes:F0} t aboard" : what;
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

        /// <summary>An order to the bot (message `seq`), carried out: the answer to give (null:
        /// talk). Marked handled for the listener only when it was carried out or answered (a
        /// report, help); a refusal ("don't know a place called ...") isn't, so the listener can
        /// still put it right.</summary>
        public string Take(string words, int fromId, int seq)
        {
            int orders = Orders;
            string reply = Command(words, fromId);
            if (string.IsNullOrEmpty(reply)) return reply;
            var chain = Clauses(Plain(words));
            var one = chain == null ? ParseOrder(words) : null;
            // ("take my ore": carried out by the speaker's game, which sends it)
            bool answered = one.HasValue && (one.Value.kind == "status" || one.Value.kind == "cargo" || one.Value.kind == "help" || one.Value.kind == "where"
                                             || one.Value.kind == "take")
                            && reply != WingmanReplies.Get("not_ready");
            if (Orders > orders || answered) Handled(seq);
            return reply;
        }

        // words said before the name ("hey wingman, ..."), spoken or typed (no one may be called
        // one: CoopWire.NameRefused)
        static readonly string[] Greetings = CoopWire.Greetings;

        /// <summary>What follows the bot's name (or "@name", "all ships", "everyone", "wingmen") at
        /// the start of a message (after a "hey" or "ok"); null if the message isn't to it.</summary>
        public static string AddressedPart(string text, string me)
        {
            text = (text ?? "").Trim();
            string to = Addressed(text, me);
            if (to != null) return to;
            // (after a greeting: "hey Wingman, ..."; the name first, for a bot called "Ok")
            foreach (var g in Greetings)
                if (text.Length > g.Length && text.StartsWith(g, StringComparison.OrdinalIgnoreCase) && !char.IsLetterOrDigit(text[g.Length]))
                    return Addressed(text.Substring(g.Length).TrimStart(',', ' ', '!'), me);
            return null;
        }

        static string Addressed(string text, string me)
        {
            string lower = text.ToLowerInvariant();
            me = (me ?? "").ToLowerInvariant();
            foreach (var call in new[] { "@" + me, me }.Concat(CoopWire.AllCalls))
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
            var chain = Clauses(Plain(words));
            if (chain != null) return Chain(chain, fromId);
            var p = ParseOrder(words);
            if (p == null)
            {
                // (a target's name alone, "Wingman, Fang Hunter": an attack on it; only a name an
                // enemy has, word for word, so talk isn't taken for one)
                var named = NamedAlone(words);
                if (named == null) return null;
                CallOffChain();
                return Engage(named);
            }
            // (a new order that takes time: whatever was still to come of the last message's is
            // called off; a report or credits on the way leave it going)
            if (!Instant(p.Value.kind)) CallOffChain();
            return Run(p.Value, fromId);
        }

        static readonly string[] PoliteLeads = { "please", "can you", "could you", "would you", "will you", "go and", "now" };
        static readonly string[] PoliteTails = { "please", "now", "for me", "for us", "thanks", "thank you" };
        static readonly HashSet<string> Politeness = new HashSet<string> { "go", "please", "thanks", "thank you", "now", "cheers" };

        /// <summary>An order without its politeness: "can you go mine please" is "mine". Leading
        /// "please", "can you", "could you", "would you", "will you", "now", "go" (not "go to",
        /// "go mining", "go after" or "go for", which are orders), and trailing "please", "now",
        /// "for me", "thanks".</summary>
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
                    && !t.StartsWith("go mining", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("go after ", StringComparison.OrdinalIgnoreCase)
                    && !t.StartsWith("go for ", StringComparison.OrdinalIgnoreCase))
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

        static readonly HashSet<string> MineTail = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "asteroids", "asteroid", "rocks", "field", "ore", "minerals" };

        /// <summary>The field's name in what follows "mine" ("at the Inner Belt": "Inner Belt";
        /// "gold at the Inner Belt": "Inner Belt"), or "" for the nearest field ("mine", "mine
        /// some asteroids", "mine the nearest rocks", "mine platinum").</summary>
        public static string MineFieldName(string target) => MineTarget(target, out _);

        // words before what it's to go after ("for some gold", "the nearest platinum"), and those
        // that ask for what's worth most ("the most valuable ore", "the best rocks", "the richest")
        static readonly HashSet<string> OreLead = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "for", "after", "some", "the", "a", "an", "any", "only", "just", "nearest", "closest", "nearby", "most", "top", "highest", "high", "very",
        };
        static readonly HashSet<string> OreWorth = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "valuable", "best", "richest", "rich", "priciest", "pricey", "expensive", "precious", "lucrative", "value", "high-value",
            "highest-value", "best-paying",
        };
        // and after it ("gold ore", "platinum-rich rocks")
        static readonly HashSet<string> OreTail = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ore", "ores", "rock", "rocks", "asteroid", "asteroids", "deposit", "deposits", "mineral", "minerals", "metal", "metals",
            "stuff", "rich", "bearing", "ones",
        };
        static readonly Regex OreRichRx = new Regex(@"(?<=\w)-(rich|bearing)\b", RxOpt);
        // where it is, not a field's name ("the best rocks in range", "platinum nearby")
        static readonly string[] HereWords = { "in range", "in sensor range", "within range", "in reach", "within reach", "nearby", "around here", "near here", "near me", "close by" };

        /// <summary>What follows "mine", taken apart: which rocks (`choice`: a mineral, the most
        /// valuable, or the nearest) and the field's name, returned ("" for the nearest field).
        /// "gold at the Inner Belt": gold, "Inner Belt"; "for the best rocks": the most valuable,
        /// ""; "the Outer Drift for platinum": platinum, "Outer Drift"; "some asteroids": the
        /// nearest, "". A mineral is matched (with its symbol, "Pt", "He-3") before a field.</summary>
        public static string MineTarget(string target, out Wingman.OreChoice choice)
        {
            string t = OreRichRx.Replace((target ?? "").Trim().TrimEnd('.', '!', '?'), " $1");
            var words = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim(',', ';')).Where(w => w.Length > 0).ToList();
            // (the rocks first: "gold at the Inner Belt", "the most valuable ore")
            int used = OreAt(words, 0, out choice);
            if (used > 0) return FieldPart(words.Skip(used));
            // (or after "for" or "after", to the end: "the Inner Belt for gold")
            for (int k = words.Count - 2; k > 0; k--)
            {
                if (!words[k].Equals("for", StringComparison.OrdinalIgnoreCase) && !words[k].Equals("after", StringComparison.OrdinalIgnoreCase)) continue;
                if (OreAt(words, k + 1, out choice) == words.Count - k - 1) return FieldPart(words.Take(k));
            }
            choice = Wingman.OreChoice.Nearest;
            return FieldPart(words);
        }

        /// <summary>Which rocks the words from `start` ask for, and how many words that took (0:
        /// they ask for none, so the nearest).</summary>
        static int OreAt(List<string> words, int start, out Wingman.OreChoice choice)
        {
            choice = Wingman.OreChoice.Nearest;
            int i = start;
            bool worth = false;
            while (i < words.Count && (OreLead.Contains(words[i]) || OreWorth.Contains(words[i])))
            {
                if (OreWorth.Contains(words[i])) worth = true;
                i++;
            }
            // (a mineral's name of up to three words: "rare earth elements", "helium 3")
            bool named = false;
            for (int n = Math.Min(3, words.Count - i); n >= 1 && !named; n--)
                if (Minerals.TryParse(string.Join(" ", words.Skip(i).Take(n)), out var m))
                {
                    choice = Wingman.OreChoice.For(m);
                    i += n;
                    named = true;
                }
            if (!named && !worth) return 0;
            if (!named) choice = Wingman.OreChoice.MostValuable;
            while (i < words.Count && OreTail.Contains(words[i])) i++;
            return i - start;
        }

        /// <summary>The field's name in words (the filler before it, whatever its order: "some
        /// asteroids in the Outer Drift", "for ore at the Inner Belt"; "asteroids" or "field"
        /// after it; "in range" or "nearby"); "" for the nearest.</summary>
        static string FieldPart(IEnumerable<string> list)
        {
            string s = string.Join(" ", list);
            foreach (var h in HereWords)
            {
                if (s.Equals(h, StringComparison.OrdinalIgnoreCase)) s = "";
                else if (s.EndsWith(" " + h, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - h.Length - 1);
            }
            var words = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            while (words.Count > 0 && MineFiller.Contains(words[0])) words.RemoveAt(0);
            while (words.Count > 1 && MineTail.Contains(words[words.Count - 1])) words.RemoveAt(words.Count - 1);
            return string.Join(" ", words);
        }

        /// <summary>The mine order's answer when it's told which rocks: "mining for platinum in
        /// Inner Belt; ...", "mining the most valuable ore at the nearest asteroid field; ...".
        /// (Not in wingman_replies.json: a mineral's name is said by the live voice.)</summary>
        public static string MineReply(Wingman.OreChoice choice, string field)
        {
            string where = string.IsNullOrEmpty(field) ? "at the nearest asteroid field" : "in " + field;
            string what = choice.kind == Wingman.OreChoice.Kind.Mineral ? $"for {choice.MineralName} {where}, the rocks richest in it"
                        : $"the most valuable ore {where}";
            return $"mining {what}; I'll sell the ore when the hold is full.";
        }

        // "attack my target", "my lock", "what I'm shooting at": what the speaker has locked
        static readonly Regex AssistRx = new Regex(
            @"^(?:(?:attack|engage|fire\s+(?:on|at)|kill|shoot(?:\s+at)?|take\s+out|destroy|hit|get|help\s+me\s+(?:with|kill))\s+)?" +
            @"(?:my\s+(?:current\s+)?(?:target|lock|locked\s+target|mark)|(?:what(?:ever)?|the\s+one|the\s+ship)\s+i(?:'m|m|\s+am)\s+" +
            @"(?:shooting(?:\s+at)?|locked\s+on(?:to)?|targeting|firing\s+(?:on|at)|attacking|fighting|aiming\s+at|on))$", RxOpt);
        // "defend me", "cover me", "help me", "I'm under attack": to the speaker, to fight what's on them
        static readonly Regex DefendRx = new Regex(
            @"^(?:(?:come\s+(?:and\s+)?)?(?:defend|cover|protect|guard|help|assist|save)\s+(?:me|us)(?:\s+out)?|back\s+me\s+up" +
            @"|(?:i(?:'m|m|\s+am)|we(?:'re|\s+are))\s+(?:under\s+(?:attack|fire)|being\s+attacked|taking\s+fire|in\s+trouble)" +
            @"|i\s+need\s+(?:help|backup|back\s*up|support|cover)|under\s+attack|mayday(?:\s+mayday)*)$", RxOpt);
        // "defend Ann", "cover Ann": another player ("cover me at the station" is the speaker)
        static readonly Regex DefendWhoRx = new Regex(@"^(?:defend|cover|protect|guard)\s+(?<who>.+)$", RxOpt);
        static readonly Regex MeFirstRx = new Regex(@"^(?:me|us)\b", RxOpt);
        // "where are you": a report
        static readonly Regex WhereRx = new Regex(
            @"^(?:where\s+(?:are|r)\s+(?:you|u|ya)(?:\s+(?:at|now))?|where(?:'re)\s+you(?:\s+at)?|where\s+you\s+at" +
            @"|what(?:'s|s|\s+is)\s+your\s+(?:position|location)|(?:your\s+)?(?:position|location)(?:\s+report)?" +
            @"|how\s+far\s+(?:away\s+|off\s+)?are\s+you(?:\s+from\s+me)?)$", RxOpt);
        // "your name is Ace", "call yourself Ace", "rename to Ace", "rename yourself Ace": a new name
        static readonly Regex RenameRx = new Regex(
            @"^(?:your\s+(?:new\s+)?name(?:'s|\s+is)(?:\s+now)?|you(?:'re|\s+are)\s+(?:now\s+)?called|call\s+yourself" +
            @"|rename\s+yourself(?:\s+(?:to|as))?|rename\s+(?:to|as)|change\s+your\s+name\s+to)\s+(?<name>\S.*)$", RxOpt);

        /// <summary>An order in words, parsed (none: talk): its kind ("chat", "follow", "regroup",
        /// "cargo", "give", "hold", "attack", "assist" (what the speaker has locked), "defend",
        /// "where", "goto", "dock", "mine", "sell", "salvage", "status", "help", "rename" (a new
        /// name for it), "giveore" (cargo out of its hold, CargoArgs), "take" (the speaker's game
        /// hands its own over)) and what it is to (a name, a place, an amount; for "mine", the
        /// rocks and the field as said, "gold at Inner Belt", which MineTarget takes apart; for
        /// "salvage", what the wreck was, or "" for the nearest: SalvageArg).</summary>
        public static (string kind, string arg)? ParseOrder(string words)
        {
            // ("can you mine please": "mine")
            // (a typed curly apostrophe as a straight one: I'm, typed on a phone)
            string w = Plain(words).Replace('\u2019', '\'').Trim().TrimEnd('?', '!', '.').TrimEnd();
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
            // (the name as it was typed, capitals and all)
            var rn = RenameRx.Match(w);
            if (rn.Success && NameIn(rn.Groups["name"].Value).Length > 0) return ("rename", NameIn(rn.Groups["name"].Value));
            // (before "attack" and "help": "attack my target", "help me")
            if (AssistRx.IsMatch(w)) return ("assist", "");
            if (DefendRx.IsMatch(w)) return ("defend", "");
            var dw = DefendWhoRx.Match(w);
            if (dw.Success) return ("defend", MeFirstRx.IsMatch(dw.Groups["who"].Value) ? "" : NoThe(dw.Groups["who"].Value.Trim()));
            if (WhereRx.IsMatch(w)) return ("where", "");
            if ((a = Arg("follow", "escort", "form up on", "form on")) != null) return ("follow", a.Length == 0 || a.ToLowerInvariant() == "me" ? "" : a);
            if (Arg("regroup", "form up", "come here", "return", "rtb") != null) return ("regroup", "");
            // (before "hold": "hold report", "what's in your hold")
            if (Arg("cargo", "manifest", "inventory", "hold report", "report hold", "hold status", "what's in your hold", "whats in your hold",
                    "what is in your hold", "what's in the hold", "whats in the hold", "what is in the hold", "how full is your hold", "how full is the hold",
                    "what are you carrying", "check your hold", "check the hold") != null)
                return ("cargo", "");
            if ((a = GiveArgs(w.TrimEnd('?', '.', '!'))) != null) return ("give", a);
            // ("give me your ore", "take my ore"; before "attack", whose "take out" isn't cargo)
            if ((a = CargoArgs(w, false)) != null) return ("giveore", a);
            if ((a = CargoArgs(w, true)) != null) return ("take", a);
            // ("salvage the wrecks", "scavenge", "salvage the wreck of the Fang Hunter")
            var sv = SalvageRx.Match(w);
            if (sv.Success) return ("salvage", SalvageArg(sv.Groups["what"].Value));
            if (Arg("hold", "stop", "wait", "stay") != null) return ("hold", "");
            if ((a = Arg("attack", "engage", "fire on", "kill", "shoot", "take out")) != null) return ("attack", NoThe(a));
            if ((a = Arg("warp to", "go to", "goto", "jump to", "warp")) != null) return ("goto", a);
            if (Arg("dock") != null) return ("dock", "");
            // ("go after helium-3", "look for platinum", "hunt for the best rocks": mining, when
            // it's ore they ask for; "go after the Skink" isn't)
            if ((a = Arg("go after", "go for", "hunt for", "look for", "search for", "prospect for", "dig for")) != null)
            {
                MineTarget(a, out var oc);
                if (oc.kind != Wingman.OreChoice.Kind.Nearest) return ("mine", NoThe(a));
            }
            if ((a = Arg("start mining", "go mining", "mine asteroids", "mine at", "mine in", "mine")) != null) return ("mine", NoThe(a));
            // ("sell at the nearest station", "sell it": wherever the nearest market is)
            if (Arg("sell the ore", "sell ore", "sell") != null) return ("sell", "");
            if (Arg("status", "report", "sitrep") != null) return ("status", "");
            if (Arg("help", "orders") != null) return ("help", "");
            return null;
        }

        // "salvage", "scavenge", "start salvaging", "strip the wreck", with what follows
        static readonly Regex SalvageRx = new Regex(@"^(?:start\s+|begin\s+)?(?:salvag(?:e|ing)|scaveng(?:e|ing)|strip(?:ping)?)(?:\s+(?<what>.+))?$", RxOpt);
        // the words after it that mean any wreck ("the wrecks", "that wreck", "for parts", "what's left")
        static readonly HashSet<string> SalvageAny = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "that", "this", "those", "these", "some", "any", "all", "every", "everything", "nearest", "closest", "near",
            "nearby", "wreck", "wrecks", "wreckage", "wreckages", "debris", "hulk", "hulks", "it", "them", "for", "parts", "goods", "scrap",
            "salvage", "stuff", "metal", "what's", "whats", "what", "is", "left", "here", "there", "around", "over", "of", "me", "us", "up",
        };

        /// <summary>What follows "salvage": "" for any wreck (the nearest: "the wrecks", "that
        /// wreck", "for parts"), else what the wreck was ("the wreck of the Fang Hunter": "Fang
        /// Hunter").</summary>
        public static string SalvageArg(string what)
        {
            string t = (what ?? "").Trim().TrimEnd('.', '!', '?').Trim();
            var words = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim(',', ';')).Where(x => x.Length > 0).ToList();
            if (words.All(x => SalvageAny.Contains(x))) return "";
            return Wingman.WreckName(t);
        }

        string Run((string kind, string arg) p, int fromId)
        {
            switch (p.kind)
            {
                case "chat": return ChatOrder(p.arg, fromId);
                case "regroup": return Order("follow", "", fromId);
                case "status": return Report();
                case "where": return Where(fromId);
                case "help": return WingmanReplies.Get("help");
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
            // (a part that's only politeness, "go and mine", "mine, then sell, thanks": left out)
            var parts = ThenRx.Split(w).Select(x => x.Trim()).Where(x => x.Length > 0 && !Politeness.Contains(x.ToLowerInvariant().TrimEnd('!', '.'))).ToList();
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
        static bool Instant(string kind) => kind == "chat" || kind == "cargo" || kind == "give" || kind == "giveore" || kind == "take" || kind == "status"
                                            || kind == "where" || kind == "help" || kind == "rename";
        static bool Standing(string kind) => kind == "follow" || kind == "regroup" || kind == "hold" || kind == "defend";

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
            // (mining and salvaging sell when the hold is full: "mine and sell" is mining)
            for (int i = orders.Count - 1; i > 0; i--)
                if (orders[i].kind == "sell" && (orders[i - 1].kind == "mine" || orders[i - 1].kind == "salvage")) orders.RemoveAt(i);
            // (one that goes on till told otherwise is over as soon as another order comes:
            // "stop and dock" is docking, "come here and follow me" following)
            for (int i = orders.Count - 2; i >= 0; i--)
                if (Standing(orders[i].kind) && orders.Skip(i + 1).Any(x => !Instant(x.kind))) orders.RemoveAt(i);
            CallOffChain();
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return WingmanReplies.Get("not_ready");
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
            // (among other orders, a new name asked for is said now: its answer comes later)
            if (answer == RenameAsked) answer = $"asking for the name {NameIn(o.arg)}";
            // (and cargo to take is said when it comes)
            if (answer == CargoComing) answer = "taking what your game sends";
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
                case "attack":
                case "assist": stepDone = () => PilotGone() ? -1 : prey == null || (prey.TryGetComponent(out Health ph) && ph.Dead) ? 1 : pilot.Current == Wingman.Order.Attack ? 0 : -1; break;
                // (mining: over with its first load sold; selling: with the sale; no longer at it before then: it couldn't)
                case "mine": stepDone = () => PilotGone() ? -1 : pilot.Sales > sales0 ? 1 : pilot.Current == Wingman.Order.Mine ? 0 : -1; break;
                case "sell": stepDone = () => PilotGone() ? -1 : pilot.Sales > sales0 ? 1 : pilot.Current == Wingman.Order.Mine ? 0 : -1; break;
                // (salvaging: over with no wrecks left, when it has gone back to its order before)
                case "salvage": stepDone = () => PilotGone() ? -1 : pilot.Current == Wingman.Order.Salvage ? 0 : pilot.SalvageFinished ? 1 : -1; break;
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
                if (CoopClient.Active) CoopChat.Say($"{Me}: couldn't {stepWords}, so not {string.Join(" or ", left)}.", Via.Order);
                return;
            }
            var n = pending[0];
            pending.RemoveAt(0);
            string answer = Start((n.kind, n.arg), n.from, out bool ok);
            if (!ok && pending.Count > 0) { pending.Clear(); answer = Sentence(answer) + " The rest called off."; }
            if (CoopClient.Active && !string.IsNullOrEmpty(answer)) CoopChat.Say($"{Me}: {answer}", Via.Order);
        }

        static string Sentence(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1) + (".!?".IndexOf(s[s.Length - 1]) >= 0 ? "" : ".");

        static string Words((string kind, string arg) o)
        {
            switch (o.kind)
            {
                case "goto": return $"warp to {o.arg}";
                case "attack": return o.arg.Length > 0 ? $"attack {o.arg}" : "attack";
                case "mine":
                {
                    string field = MineTarget(o.arg, out var oc);
                    string what = oc.kind == Wingman.OreChoice.Kind.Mineral ? $" for {oc.MineralName}" : oc.kind == Wingman.OreChoice.Kind.MostValuable ? " the most valuable ore" : "";
                    return $"mine{what}{(field.Length > 0 ? $" at {field}" : "")}";
                }
                case "follow": return o.arg.Length > 0 ? $"follow {o.arg}" : "follow";
                case "salvage": return o.arg.Length > 0 ? $"salvage the wreck of {o.arg}" : "salvage";
                case "assist": return "attack your target";
                case "defend": return o.arg.Length > 0 ? $"defend {o.arg}" : "defend you";
                case "where": return "say where I am";
                case "give": return "give credits";
                case "giveore": return "hand the cargo over";
                case "take": return "take your cargo";
                case "rename": return $"take the name {o.arg}";
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

        // ore handed over: "give me your ore", "hand over the iron", "send Ann 100 t of gold"; and
        // taken, "take my ore" ("take out the Skink" is an attack: "out" isn't cargo)
        static readonly Regex HandRx = new Regex(@"^(?:please\s+)?(?:give|transfer|send|hand\s+over|hand|pass)\b(?<rest>.*)$", RxOpt);
        static readonly Regex TakeRx = new Regex(@"^(?:please\s+)?take\b(?<rest>.*)$", RxOpt);
        // ("100", "1,500", "2k", "100t")
        static readonly Regex TonnesRx = new Regex(@"^(?<n>\d[\d,]*(?:\.\d+)?)(?<k>k)?t?$", RxOpt);
        static readonly HashSet<string> CargoLead = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "your", "my", "our", "of", "that", "this", "those", "these", "t", "tonne", "tonnes", "ton", "tons",
        };
        static readonly HashSet<string> CargoOre = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ore", "ores", "cargo", "minerals", "mineral", "rocks", "load", "haul",
        };

        // the parts aboard, all of them ("give me the parts")
        static readonly HashSet<string> CargoParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "parts", "ship parts", "salvaged parts", "salvage parts", "spare parts", "part",
        };

        static bool CargoWord(string w) => CargoLead.Contains(w) || CargoOre.Contains(w) || TonnesRx.IsMatch(w) || Minerals.TryParse(w, out _)
                                           || w.Equals("all", StringComparison.OrdinalIgnoreCase) || w.Equals("half", StringComparison.OrdinalIgnoreCase);

        /// <summary>An order to hand cargo over (`take` false: "give me your ore", "give me the
        /// platinum", "give me 100 t of iron", "hand over half your gold", "give Ann the iron") or
        /// to take it ("take my ore", "take the iron", "take 50 t of nickel"), as the order's
        /// target: "&lt;amount&gt; &lt;item&gt;[ to &lt;name&gt;]", the amount tonnes, "all",
        /// "half" or "?" (not said: "give me some iron"), the item "ore" (all the ore), "parts"
        /// (the parts aboard: "give me the parts") or a mineral's id ("Iron"). Null if it isn't one
        /// (credits are GiveArgs').</summary>
        public static string CargoArgs(string words, bool take)
        {
            var m = (take ? TakeRx : HandRx).Match((words ?? "").Trim().TrimEnd('?', '!', '.').Trim());
            if (!m.Success) return null;
            string rest = m.Groups["rest"].Value.Trim();
            string to = "";
            if (!take)
            {
                var tm = ToRx.Match(rest);
                if (tm.Success) { to = tm.Groups["to"].Value.Trim(); rest = rest.Substring(0, tm.Index).Trim(); }
            }
            var w = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim(',', ';')).Where(x => x.Length > 0).ToList();
            if (!take && w.Count > 0)
            {
                // ("give me ...", or a name first: "give Ann the iron")
                if (w[0].Equals("me", StringComparison.OrdinalIgnoreCase) || w[0].Equals("us", StringComparison.OrdinalIgnoreCase)) w.RemoveAt(0);
                else if (w.Count > 1 && Array.IndexOf(NotNames, w[0].ToLowerInvariant()) < 0 && !CargoWord(w[0]) && CargoWord(w[1]))
                {
                    to = w[0];
                    w.RemoveAt(0);
                }
            }
            if (Array.IndexOf(NotNames, to.ToLowerInvariant()) >= 0) to = "";
            string amount = "all";
            bool said = false, everything = false;
            int i = 0;
            for (; i < w.Count; i++)
            {
                string x = w[i].ToLowerInvariant();
                var tn = TonnesRx.Match(x);
                if (tn.Success && double.TryParse(tn.Groups["n"].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                {
                    amount = (tn.Groups["k"].Success ? n * 1000.0 : n).ToString("0.##", CultureInfo.InvariantCulture);
                    said = true;
                    continue;
                }
                if (x == "all") { amount = "all"; said = true; continue; }
                if (x == "everything") { amount = "all"; said = everything = true; continue; }
                if (x == "half") { amount = "half"; said = true; continue; }
                if (x == "some" || x == "any") { if (!said) amount = "?"; continue; }
                if (CargoLead.Contains(x)) continue;
                break;
            }
            var what = w.Skip(i).ToList();
            string item;
            if (what.Count == 0)
            {
                if (!everything) return null;
                item = "ore";
            }
            else if (what.All(x => CargoOre.Contains(x))) item = "ore";
            else if (CargoParts.Contains(string.Join(" ", what))) item = "parts";
            else
            {
                // ("iron ore", "platinum rocks")
                while (what.Count > 1 && CargoOre.Contains(what[what.Count - 1])) what.RemoveAt(what.Count - 1);
                if (!Minerals.TryParse(string.Join(" ", what), out var mineral)) return null;
                item = mineral.ToString();
            }
            return $"{amount} {item}{(to.Length > 0 ? $" to {to}" : "")}";
        }

        /// <summary>What a hold has aboard, in a line: how full, the cargo (largest first), the credits.</summary>
        public static string CargoLine(CargoHold hold, int most = 5)
        {
            if (hold == null) return WingmanReplies.Get("no_hold");
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
            // (a name needs no ship)
            if ((order ?? "").Trim().Equals("rename", StringComparison.OrdinalIgnoreCase)) return Rename(target, fromId);
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return WingmanReplies.Get("not_ready");
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
                    if (who == null) return WingmanReplies.Get("nobody_to_follow");
                    Pilot.Follow(who.id);
                    lastLeader = who.id;
                    Orders++;
                    return $"following {who.name}.";
                }
                case "hold":
                    Pilot.Hold();
                    lastLeader = 0;
                    Orders++;
                    return WingmanReplies.Get("holding");
                case "attack":
                {
                    Vector3 at = cc.transform.position;
                    // (by name, as heard: "Frank Hunter" is the Fang Hunter, and the answer says so)
                    if (target.Length > 0)
                    {
                        var named = Wingman.FindTargetLoose(target, at);
                        return named != null ? Engage(named) : $"no target called {target} in sight.";
                    }
                    var t = Wingman.NearestEnemy(at, 30000f);
                    if (t != null) return Engage(t);
                    // (nothing near it, but its leader, or whoever asked, is in a fight: that one)
                    var (fight, whose) = FightOf(fromId);
                    return fight != null ? Engage(fight, $"nothing near me; going to {whose} fight: ") : "no enemies in sight.";
                }
                case "assist":
                    return Assist(target, fromId);
                case "defend":
                    return Defend(target, fromId);
                case "goto":
                {
                    var p = Place(target);
                    if (p == null) return target.Length > 0 ? $"don't know a place called {target}." : "go where?";
                    Pilot.Goto(p);
                    lastLeader = 0;
                    Orders++;
                    return WingmanReplies.Get("heading", ("place", p.displayName));
                }
                case "dock":
                    Pilot.Dock();
                    lastLeader = 0;
                    Orders++;
                    return WingmanReplies.Get("docking");
                case "mine":
                {
                    // (at a field by name, or the nearest; "asteroids" is any field)
                    WarpPoint field = null;
                    // ("asteroids in the Outer Drift", "at the Inner Belt": the field's name, if any;
                    // "rocks", "some asteroids", "the nearest field": the nearest; and which rocks
                    // there: "platinum", "the most valuable ore", or the nearest)
                    string name = MineTarget(target, out var choice);
                    if (name.Length > 0)
                    {
                        field = Place(name);
                        if (field == null || field.kind != WarpPoint.Kind.AsteroidField)
                            return choice.kind == Wingman.OreChoice.Kind.Nearest && name.IndexOf(' ') < 0
                                ? $"don't know an asteroid field or a mineral called {name}." : $"don't know an asteroid field called {name}.";
                    }
                    Pilot.Mine(field, choice);
                    lastLeader = 0;
                    Orders++;
                    if (choice.kind != Wingman.OreChoice.Kind.Nearest) return MineReply(choice, field != null ? field.displayName : null);
                    return field != null ? WingmanReplies.Get("mine_field", ("field", field.displayName)) : WingmanReplies.Get("mine_nearest");
                }
                case "sell":
                {
                    var hold = cc.GetComponentInChildren<CargoHold>();
                    if (hold == null || hold.OreTonnes < 0.5f) return WingmanReplies.Get("no_ore");
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
                    if (hold == null || hold.Credits < 1f) return WingmanReplies.Get("no_credits");
                    var c = CoopClient.Instance;
                    if (c == null || !CoopClient.Active) return WingmanReplies.Get("not_connected");
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
                    if (who == null) return WingmanReplies.Get("nobody_to_give");
                    if (IsBot(who)) return $"{who.name} is a bot: credits go to players.";
                    var mine = c.Players.FirstOrDefault(p => p != null && p.id == c.MyId);
                    if (mine != null && who.level != mine.level) return $"{who.name} isn't in this part of space: credits go to players here.";
                    float amount;
                    string aw = amountWord.ToLowerInvariant();
                    if (aw == "?") return $"how many credits? I've {hold.Credits:N0} cr (\"give me 500 credits\", or \"give me all your credits\").";
                    if (aw == "" || aw == "all" || aw == "everything") amount = hold.Credits;
                    else if (aw == "half") amount = hold.Credits / 2f;
                    else if (!float.TryParse(aw.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out amount) || !(amount >= 1f))
                        return WingmanReplies.Get("how_many");
                    amount = Mathf.Floor(amount);
                    if (amount < 1f) return WingmanReplies.Get("no_whole_credit");
                    if (amount > hold.Credits + 1e-3f) return $"I've only {hold.Credits:N0} cr.";
                    if (!CoopGifts.Give(hold, who.id, amount)) return WingmanReplies.Get("gift_failed");
                    Orders++;
                    return $"transferred {amount:N0} cr to {who.name}; {hold.Credits:N0} cr left.";
                }
                case "giveore":
                    return GiveCargo(cc, target, fromId);
                case "salvage":
                    return SalvageOrder(cc, target);
                case "take":
                    // (the speaker's game sends it, with the order; it's said when it comes)
                    return fromId == 0 ? $"a player's own game hands its cargo over: \"{Me}, take my ore\" on the comms." : CargoComing;
                default:
                    return $"don't understand '{order}'.";
            }
        }

        // ------------------------------------------------------------ cargo handed over

        /// <summary>What a "take my ore" answers at once: nothing yet (the speaker's game sends the
        /// cargo with its order, and it's said when it comes: OnCargoArrived).</summary>
        public const string CargoComing = "(cargo coming)";

        /// <summary>"give me your ore", "give Ann the platinum": out of its hold to that player
        /// (whoever asked, if none is named), a person in this level within 50 km (CoopCargo).
        /// `target`: "&lt;amount&gt; &lt;item&gt;[ to &lt;name&gt;]" (CargoArgs).</summary>
        string GiveCargo(CarrierController cc, string target, int fromId)
        {
            var hold = cc.GetComponentInChildren<CargoHold>();
            if (hold == null) return WingmanReplies.Get("no_hold");
            var c = CoopClient.Instance;
            if (c == null || !CoopClient.Active) return "not connected to a co-op server.";
            CoopCargo.SplitCargo(target, out string amountWord, out string item, out string name);
            CoopPlayer who;
            if (name.Length > 0 && !name.Equals("me", StringComparison.OrdinalIgnoreCase) && !name.Equals("us", StringComparison.OrdinalIgnoreCase))
            {
                who = c.Players.FirstOrDefault(p => p != null && p.id != c.MyId && p.name.ToLowerInvariant().Contains(name.ToLowerInvariant()));
                if (who == null) return $"there's no player called {name}.";
            }
            else who = c.Players.FirstOrDefault(p => p != null && p.id != c.MyId && !IsBot(p) && p.id == Asker(fromId))
                       ?? NearestHuman(cc.transform.position);
            if (who == null) return "there's nobody to hand cargo to.";
            if (IsBot(who) || CoopCargo.IsAi(who, c.Players)) return $"{who.name} is a bot: cargo goes to players.";
            var mine = c.Players.FirstOrDefault(p => p != null && p.id == c.MyId);
            if (mine != null && who.level != mine.level) return $"{who.name} isn't in this part of space: cargo goes to players here.";
            // ("give me the parts": those aboard now, and those it salvages from now on, kept for them)
            bool parts = item.Equals("parts", StringComparison.OrdinalIgnoreCase);
            if (parts)
            {
                if (amountWord == "?") amountWord = "all";
                Pilot.PartsFor = who.id;
                nextPartsTry = Time.realtimeSinceStartup + CoopCargo.ReceiptWait;
            }
            var want = CoopCargo.Wanted(hold, amountWord, item, out string why);
            if (want == null)
            {
                if (parts)
                {
                    Orders++;
                    return $"no parts aboard yet, {who.name}: I'll keep the ones I salvage for you, and hand them over when you're within {CoopCargo.Reach / 1000f:F0} km.";
                }
                if (why != "how much?") return why + ".";
                bool ore = item.Equals("ore", StringComparison.OrdinalIgnoreCase);
                string what = ore ? "ore" : Minerals.TryParse(item, out var m) ? Minerals.Name(m).ToLowerInvariant() : item;
                float have = ore ? hold.OreTonnes : Minerals.TryParse(item, out var m2) ? hold[m2] : hold.Amount(item);
                if (have < 0.005f) return $"no {what} aboard.";
                return $"how much? I've {have.ToString(have >= 9.95f ? "F0" : "0.#", CultureInfo.InvariantCulture)} t of {what} " +
                       $"(\"give me 50 t of {what}\", or \"give me all your {what}\").";
            }
            string refused = CoopCargo.Send(hold, who.id, want, out var sent);
            if (refused.Length > 0)
            {
                if (!parts) return $"can't hand it to {who.name}: {refused}.";
                Orders++;
                return $"can't hand the parts over now ({refused}); I'll keep them for you, {who.name}, and the ones I salvage, till I can.";
            }
            Orders++;
            return $"sending {CoopCargo.Describe(sent)} to {who.name}; hold {hold.Used:F0} of {hold.capacity:F0} t now." +
                   (parts ? " I'll keep the parts I salvage for you too." : "");
        }

        // when the parts kept for a player are next offered to them
        float nextPartsTry;

        /// <summary>The parts kept for a player ("give me the parts"): handed over when they're in
        /// this level and within 50 km (tried every 5 s at most, and a minute after any came back:
        /// a full hold, say). A player who has left has none kept: they're sold again.</summary>
        void HandOverParts()
        {
            var pl = Pilot;
            if (pl == null || pl.PartsFor == 0 || !CoopClient.Active) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextPartsTry) return;
            nextPartsTry = now + 5f;
            var c = CoopClient.Instance;
            var who = c.Players.FirstOrDefault(p => p != null && p.id == pl.PartsFor);
            if (who == null) { pl.PartsFor = 0; return; }
            if (CoopCargo.Outstanding > 0) return;
            var cc = pl.demo != null ? pl.demo.carrier : null;
            var hold = cc != null ? cc.GetComponentInChildren<CargoHold>() : null;
            if (hold == null || cc.Destroyed) return;
            var r = CoopWorld.Instance != null ? CoopWorld.Instance.RemoteOf(who.id) : null;
            if (r == null || r.Hidden || Vector3.Distance(r.transform.position, cc.transform.position) > CoopCargo.Reach) return;
            var want = CoopCargo.Wanted(hold, "all", "parts", out _);
            if (want == null) return;
            if (CoopCargo.Send(hold, who.id, want, out var sent).Length > 0) return;
            CoopChat.Say($"{Me}: handing you {CoopCargo.Describe(sent)}, {who.name}.", Via.Event);
        }

        /// <summary>"salvage", "salvage the wreck of the Fang Hunter": the wreck named (what it
        /// was, by name as a target's is matched), else the nearest within 30 km, then the others
        /// near it (Wingman.SalvageWrecks). No wreck: says so, and nothing changes.</summary>
        string SalvageOrder(CarrierController cc, string target)
        {
            if (cc.GetComponentInChildren<CargoHold>() == null) return WingmanReplies.Get("no_hold");
            Vector3 at = cc.transform.position;
            string name = Wingman.WreckName(target);
            Wreck w;
            if (name.Length > 0)
            {
                w = Wingman.WreckNamed(name, at);
                if (w == null) return $"no wreck of {name} to salvage.";
            }
            else
            {
                w = Wingman.NearestWreck(at);
                // (salvaging already, back from a sale say: carried on, from where it was)
                if (w == null && Pilot.Current != Wingman.Order.Salvage) return "nothing to salvage within 30 km: no wrecks.";
            }
            Pilot.SalvageWrecks(w, name);
            Orders++;
            if (w == null) return "salvaging still.";
            float d = Vector3.Distance(at, w.transform.position);
            Vector3 there = w.transform.position;
            int more = Wreck.All.Count(x => x != null && !x.Empty && x != w && (x.transform.position - there).sqrMagnitude < Salvage.Around * Salvage.Around);
            return $"salvaging the wreck of {w.Of}, {HowFar(d)} off{(more > 0 ? $", then {more} more near it" : "")}; I'll sell what I take when the hold is full.";
        }

        /// <summary>Cargo from a player has come (or been refused): said on the comms.</summary>
        void OnCargoArrived(CoopPlayer from, List<CoopItem> loaded, List<CoopItem> left, string why)
        {
            if (!CoopClient.Active || from == null) return;
            var cc = Pilot != null && Pilot.demo != null ? Pilot.demo.carrier : null;
            var hold = cc != null ? cc.GetComponentInChildren<CargoHold>() : null;
            string now = hold != null ? $"; hold {hold.Used:F0} of {hold.capacity:F0} t" : "";
            string line = why.Length > 0 ? $"couldn't take your cargo, {from.name}: {why}. It goes back to you."
                        : loaded.Count == 0 ? $"no room for {CoopCargo.Describe(left)}, {from.name}: it goes back to you."
                        : $"took {CoopCargo.Describe(loaded)} from {from.name}{(left.Count > 0 ? $"; no room for {CoopCargo.Describe(left)}, it goes back to you" : "")}{now}.";
            CoopChat.Say($"{Me}: {line}", Via.Event);
        }

        /// <summary>Cargo it sent has been answered: said on the comms when any came back.</summary>
        void OnCargoReturned(int to, List<CoopItem> taken, List<CoopItem> back, string why)
        {
            // (parts kept for a player that came back: offered again in a minute, not every 5 s)
            if (back.Count > 0 && Pilot != null && to == Pilot.PartsFor) nextPartsTry = Time.realtimeSinceStartup + 60f;
            if (!CoopClient.Active || (back.Count == 0 && why.Length == 0)) return;
            string name = CoopClient.Instance.NameOf(to);
            string line = why == "no answer" ? $"no answer from {name}'s game: {CoopCargo.Describe(back)} back aboard."
                        : why.Length > 0 ? $"{name}'s game {why}: {CoopCargo.Describe(back)} back aboard."
                        : $"{name}'s hold took {CoopCargo.Describe(taken)}; no room for the rest: {CoopCargo.Describe(back)} back aboard.";
            CoopChat.Say($"{Me}: {line}", Via.Event);
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

        // ------------------------------------------------------------ the fights

        /// <summary>A distance as a pilot says it: "850 m", "1.7 km", "12,400 km", "2.1 million km".</summary>
        public static string HowFar(float metres)
        {
            var inv = CultureInfo.InvariantCulture;
            if (metres < 1000f) return Mathf.Max(0f, metres).ToString("F0", inv) + " m";
            float km = metres / 1000f;
            if (km < 10f) return km.ToString("0.0", inv) + " km";
            if (km < 1e6f) return km.ToString("N0", inv) + " km";
            return (km / 1e6f).ToString("0.0", inv) + " million km";
        }

        /// <summary>Attack a target: the answer, with its name as it is (whatever it was called)
        /// and, far off, how far (the pilot warps to it).</summary>
        string Engage(Transform t, string lead = "")
        {
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return WingmanReplies.Get("not_ready");
            float d = Vector3.Distance(Pilot.demo.carrier.transform.position, t.position);
            Pilot.Attack(t);
            Orders++;
            return $"{lead}engaging {Wingman.NameOf(t)}{(d > Pilot.warpAfter ? $" ({HowFar(d)} off)" : "")}.";
        }

        /// <summary>The enemy a message is only the name of ("Fang Hunter", "the Skink"), or null.</summary>
        Transform NamedAlone(string words)
        {
            var cc = Pilot != null && Pilot.demo != null ? Pilot.demo.carrier : null;
            if (cc == null) return null;
            return Wingman.EnemyNamed(Plain(words).Trim().TrimEnd('?', '!', '.'), cc.transform.position);
        }

        /// <summary>Whoever gave an order (from the API: the last player to speak).</summary>
        int Asker(int fromId) => fromId != 0 ? fromId : lastSpeaker;

        /// <summary>The player an order is about: the one named (part of the name), else whoever
        /// gave it, else the nearest player. `refusal` says why there's none.</summary>
        CoopPlayer PlayerFor(string name, int fromId, out string refusal)
        {
            refusal = null;
            var c = CoopClient.Instance;
            if (c == null) return null;
            name = (name ?? "").Trim();
            if (name.Length > 0 && !name.Equals("me", StringComparison.OrdinalIgnoreCase) && !name.Equals("us", StringComparison.OrdinalIgnoreCase))
            {
                var named = c.Players.FirstOrDefault(p => p != null && p.id != c.MyId && p.name.ToLowerInvariant().Contains(name.ToLowerInvariant()));
                if (named == null) refusal = $"there's no player called {name}.";
                return named;
            }
            int asker = Asker(fromId);
            var cc = Pilot != null && Pilot.demo != null ? Pilot.demo.carrier : null;
            return c.Players.FirstOrDefault(p => p != null && p.id != c.MyId && p.id == asker)
                   ?? (cc != null ? NearestHuman(cc.transform.position) : null);
        }

        /// <summary>"attack my target": what the player's game says they have locked (an enemy's
        /// id, the same in every game), warping to it if it's far. Never a player: their game
        /// never sends one.</summary>
        string Assist(string name, int fromId)
        {
            var who = PlayerFor(name, fromId, out string refusal);
            if (who == null) return refusal ?? "whose target?";
            bool asker = who.id == Asker(fromId);
            string your = asker ? "your" : who.name + "'s";
            var w = CoopWorld.Instance;
            var r = w != null ? w.RemoteOf(who.id) : null;
            if (r == null) return $"can't see {your} ship from here.";
            // (a game too old to say: not "nothing locked", which it can't tell)
            if (r.ContentVersion < CoopWire.ContentTarget)
                return $"{your} game doesn't say what {(asker ? "you've" : who.name + "'s")} locked: name it (\"attack the Skink\").";
            string id = r.TargetId ?? "";
            if (id.Length == 0) return $"{(asker ? "you've" : who.name + " has")} nothing locked.";
            if (id.StartsWith(CoopWorld.NotEnemyLock))
            {
                string what = id.Substring(CoopWorld.NotEnemyLock.Length);
                return what.Length > 0 ? $"that's not an enemy: {what}." : "that's not an enemy.";
            }
            if (id.StartsWith(CoopWorld.OwnEnemyLock)) return $"can't see {id.Substring(CoopWorld.OwnEnemyLock.Length)} from here.";
            var t = w.EntityTransform(id);
            if (t == null || !t.gameObject.activeInHierarchy) return $"can't see {your} target from here.";
            if (t.TryGetComponent(out Health h) && h.Dead) return $"{Wingman.NameOf(t)} is already destroyed.";
            if (!(t.TryGetComponent(out TargetInfo info) && info.Hostile)) return $"that's not an enemy: {Wingman.NameOf(t)}.";
            return Engage(t);
        }

        /// <summary>"defend me": to the player from wherever it is, to fight what's on them. The
        /// answer says how it goes: with them, on its way, or fighting where it is till the warp
        /// drive will go ("fighting here, coming when clear": the drive won't under fire).</summary>
        string Defend(string name, int fromId)
        {
            var cc = Pilot.demo.carrier;
            var who = PlayerFor(name, fromId, out string refusal);
            if (who == null) return refusal ?? "there's nobody to defend.";
            bool busy = Pilot.Fighting;
            Pilot.Defend(who.id);
            lastLeader = who.id;
            Orders++;
            bool asker = who.id == Asker(fromId);
            string you = asker ? "you" : who.name;
            var w = CoopWorld.Instance;
            var r = w != null ? w.RemoteOf(who.id) : null;
            if (r == null || r.Hidden) return $"coming to {you} as soon as I can see {(asker ? "your" : who.name + "'s")} ship.";
            float d = Vector3.Distance(cc.transform.position, r.transform.position);
            var threat = Wingman.ThreatTo(r);
            string onYou = threat != null ? $", {Wingman.NameOf(threat)} on {you}" : "";
            if (d <= Pilot.warpAfter)
                return threat != null ? $"with {you}: engaging {Wingman.NameOf(threat)}." : $"with {you} ({HowFar(d)} off); nothing on {you} that I can see.";
            var drive = cc.GetComponent<WarpDrive>();
            if (drive != null && drive.Current != WarpDrive.State.Ready) return $"coming to {you} when I'm out of warp ({HowFar(d)} off{onYou}).";
            if (drive != null && !drive.CanWarp(out string why))
                return busy || drive.InCombat(out _) ? $"fighting here, coming when clear ({HowFar(d)} off{onYou})." : $"coming to {you}, but I can't warp: {why} ({HowFar(d)} off).";
            return $"on my way to {you}, {HowFar(d)} off{onYou}.";
        }

        /// <summary>A fight its leader (or whoever asked) is in: what they've locked, or the
        /// nearest enemy within 30 km of them, and whose fight it is ("your", "Ann's"); none if
        /// neither is in one, or their ship isn't seen.</summary>
        (Transform fight, string whose) FightOf(int fromId)
        {
            var w = CoopWorld.Instance;
            var c = CoopClient.Instance;
            if (w == null || c == null || Pilot == null) return (null, "");
            int asker = Asker(fromId);
            foreach (int id in new[] { Pilot.LeaderId, asker })
            {
                if (id == 0 || id == c.MyId) continue;
                var t = Wingman.ThreatTo(w.RemoteOf(id), 30000f);
                if (t != null) return (t, id == asker ? "your" : c.NameOf(id) + "'s");
            }
            return (null, "");
        }

        /// <summary>Where it is ("near Havilah, 1.7 km from you; closing on Ann (1.7 km)"): the
        /// nearest place, how far from whoever asked (when their ship is seen), and what it's doing.</summary>
        string Where(int fromId)
        {
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return WingmanReplies.Get("not_ready");
            Vector3 at = Pilot.demo.carrier.transform.position;
            var near = WarpPoint.Nearest(at);
            string place = near != null ? $"near {near.displayName}" : "out in open space";
            var w = CoopWorld.Instance;
            int id = Asker(fromId);
            var r = w != null && id != 0 ? w.RemoteOf(id) : null;
            string off = r != null && !r.Hidden ? $", {HowFar(Vector3.Distance(at, r.transform.position))} from you" : "";
            return $"{place}{off}; {Pilot.Doing}.";
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
            if (Pilot == null || Pilot.demo == null || Pilot.demo.carrier == null) return WingmanReplies.Get("not_ready");
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
                        if (Chat != was && CoopClient.Active) CoopChat.Say($"{Me}: {ChatModeLine()}", Via.Event);
                        json = ChatModeJson($"\"ok\":true,\"previous\":{Q(ModeName(was))},");
                        break;
                    }
                    case "/say" when post:
                    {
                        var b = Parse<SayBody>(body);
                        bool ok = b != null && CoopChat.Say(b.text, Via.Llm);
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
                        // (a new name: said on the comms, in it, when the server answers)
                        if (answer == RenameAsked) answer = $"asked the server for the name {NameIn(b?.target)}; I'll say it on the comms when it answers.";
                        else if (ok && CoopClient.Active) CoopChat.Say($"{Me}: {answer}", Via.Order);
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
                          (Pilot.Current == Wingman.Order.Mine ? $",\"step\":{Q(Pilot.Step.ToString().ToLowerInvariant())},\"sold_t\":{F(Pilot.TonnesSold)},\"earned\":{F(Pilot.CreditsEarned)},\"sales\":{Pilot.Sales}" + MineJson() : "") +
                          (Pilot.Current == Wingman.Order.Salvage ? SalvageJson(cc) : "") +
                          $",\"parts_for\":{Q(Pilot.PartsFor != 0 && c != null ? c.NameOf(Pilot.PartsFor) : "")}" +
                          $",\"line\":{Q(DoingLine())}}}");
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
                // the wrecks to salvage (30 km), nearest first
                sb.Append(",\"wrecks\":[");
                first = true;
                foreach (var w in Wreck.All.Where(w => w != null && !w.Empty && (w.transform.position - at).sqrMagnitude < Salvage.Around * Salvage.Around)
                                           .OrderBy(w => (w.transform.position - at).sqrMagnitude).Take(8))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"{{\"name\":{Q(w.Name)},\"km\":{F(Vector3.Distance(w.transform.position, at) / 1000f)},\"t\":{F(w.Tonnes)},\"holds\":{Q(w.Summary())}}}");
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

        /// <summary>The mine order's choice for the status: "ore" (the mineral's name, "most
        /// valuable", or "" for the nearest rocks), "ore_missing" (none of the mineral in range,
        /// so the most valuable), and the rock it's at ("rock", "rock_mix": "Fe 61%  Ni 22%  Pt 1.1%").</summary>
        string MineJson()
        {
            var sb = new StringBuilder($",\"ore\":{Q(Pilot.MineChoice.Key)},\"ore_missing\":{B(Pilot.OreMissing)}");
            var rock = Pilot.Rock;
            if (rock != null && rock.Valid)
                sb.Append($",\"rock\":{Q(rock.Label)},\"rock_mix\":{Q(Minerals.Summary(rock.Mix, 3, false))},\"rock_t\":{F(rock.Remaining)}");
            return sb.ToString();
        }

        /// <summary>The salvage order for the status: "step", the wreck it's at ("wreck", "wreck_t"
        /// left in it, "wreck_km"), and the run's "salvaged_t", "earned" and "sales".</summary>
        string SalvageJson(CarrierController cc)
        {
            var w = Pilot.SalvageTarget;
            var sb = new StringBuilder($",\"step\":{Q(Pilot.SalvagingStep.ToString().ToLowerInvariant())},\"wreck\":{Q(w != null ? w.Name : "")}");
            if (w != null) sb.Append($",\"wreck_t\":{F(w.Tonnes)},\"wreck_km\":{F(Vector3.Distance(w.transform.position, cc.transform.position) / 1000f)}");
            sb.Append($",\"salvaged_t\":{F(Pilot.SalvageTaken)},\"earned\":{F(Pilot.SalvageCredits)},\"sales\":{Pilot.SalvageSales}");
            return sb.ToString();
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
                bool toMe = !mine && (AddressedPart(l.text, Me) != null || UnnamedCall(l) != null);
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
