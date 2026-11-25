using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The connection to a co-op server. It outlives the level (a restart keeps the game
    /// connected): it says hello, tells the server which level it is in, keeps the list of
    /// players and the room (who hosts it, and its orbit clock), and keeps its own clock in step
    /// with the server's by pinging it. Game messages go to the level's CoopWorld.
    /// </summary>
    public class CoopClient : MonoBehaviour
    {
        public const int Protocol = CoopWire.Protocol;
        public static CoopClient Instance { get; private set; }

        /// <summary>Connected, welcomed, and playing together.</summary>
        public static bool Active => Instance != null && Instance.Connected;

        public bool Connected => socket != null && socket.Status == CoopSocket.State.Open && MyId > 0;
        public bool Connecting => socket != null && socket.Status != CoopSocket.State.Closed && MyId == 0;
        public string Url { get; private set; } = "";
        /// <summary>What is going on, in a few words (for the menus).</summary>
        public string Status { get; private set; } = "";
        public int MyId { get; private set; }
        /// <summary>The server keeps a log of the comms channel (it said so in its welcome).</summary>
        public bool ChatLogged { get; private set; }
        public string MyName { get; private set; } = "";
        public int MaxPlayers { get; private set; }
        public int HostId { get; private set; }
        public string RoomLevel { get; private set; } = "";
        public double Epoch { get; private set; }
        public readonly List<CoopPlayer> Players = new List<CoopPlayer>();
        /// <summary>The round trip to the server, seconds.</summary>
        public double Rtt => clock.Rtt;
        /// <summary>Am I the one whose game runs the enemies in my level?</summary>
        public bool IsHost => Connected && HostId == MyId;
        public double ServerNow => Time.realtimeSinceStartupAsDouble + clock.Offset;
        /// <summary>The room's orbit clock: the same in every game in the room.</summary>
        public double OrbitClock => ServerNow - Epoch;

        /// <summary>Raised for every game message, and for the server's roster, joins and
        /// leaves (not its other messages, which the connection keeps to itself).</summary>
        public static event Action<CoopMsg> Message;
        public static event Action RoomChanged;
        /// <summary>The answer to Rename: taken or not, the name I have now, and why not. A
        /// server too old to rename never answers: after RenameWait seconds it is taken as
        /// refused, with a "why" that says so.</summary>
        public static event Action<bool, string, string> Renamed;

        /// <summary>How long Rename waits for the server's answer.</summary>
        public const float RenameWait = 6f;
        /// <summary>The name asked for and not answered yet (null: none).</summary>
        public string Renaming { get; private set; }
        double renameUntil;

        CoopSocket socket;
        readonly ClockSync clock = new ClockSync();
        double nextPing;
        bool helloSent;
        // where to try next if the first address never answers (null: nowhere)
        string fallbackUrl;

        /// <summary>Connect (closing any connection there is): address as a host name or IP
        /// (or a full ws:// or wss:// address), and the port, with the server's API key (null:
        /// the one CoopKey has for the name).</summary>
        public static void Connect(string address, int port, string name, string key = null)
        {
            if (Instance == null)
            {
                var go = new GameObject("Co-op connection");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<CoopClient>();
            }
            Instance.Open(address, port, name, key ?? CoopKey.For(name));
        }

        string key = "";

        /// <summary>The server's WebSocket address: ws://host:port/coop (wss:// when `secure`, an
        /// IPv6 address in brackets), or a full ws:// or wss:// address as given, with /coop
        /// added when it has no path.</summary>
        internal static string UrlFor(string address, int port, bool secure)
        {
            address = (address ?? "").Trim();
            if (address.StartsWith("ws://") || address.StartsWith("wss://"))
                return address.Substring(address.IndexOf("//") + 2).Contains("/") ? address : address + "/coop";
            string host = address.Contains(":") && !address.StartsWith("[") ? $"[{address}]" : address;
            return $"{(secure ? "wss" : "ws")}://{host}:{port}/coop";
        }

        /// <summary>For a page served over https from the co-op server's own host, but told a
        /// different port (the server's plain-socket 7777, say, which can't take wss://): the
        /// page's own address, wss://host[:port]/coop, where a proxy may pass sockets on to the
        /// server. Null when there is nothing else to try.</summary>
        internal static string PageOriginUrl(string address, int port, string pageUrl)
        {
            address = (address ?? "").Trim();
            if (string.IsNullOrEmpty(pageUrl) || !pageUrl.StartsWith("https:") ||
                address.StartsWith("ws://") || address.StartsWith("wss://")) return null;
            Uri page;
            try { page = new Uri(pageUrl); } catch { return null; }
            if (!string.Equals(address.Trim('[', ']'), page.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)) return null;
            if (port == page.Port) return null;
            return $"wss://{page.Authority}/coop";
        }

        public static void Disconnect()
        {
            if (Instance != null) Instance.Close("disconnected");
        }

        void Open(string address, int port, string name, string apiKey)
        {
            Close("");
            key = (apiKey ?? "").Trim();
            address = (address ?? "").Trim();
            MyName = string.IsNullOrWhiteSpace(name) ? "Captain" : name.Trim();
            // a page served over https may only open wss:// sockets
            bool secure = LaunchArgs.PageUrl.StartsWith("https:");
            string url = UrlFor(address, port, secure);
            fallbackUrl = LaunchArgs.OnWeb ? PageOriginUrl(address, port, LaunchArgs.PageUrl) : null;
            Url = url;
            Status = $"connecting to {url}...";
            Debug.Log($"[Co-op] connecting to {url}");
            socket = CoopSocket.Open(url);
            helloSent = false;
            clock.Reset();
        }

        void Close(string why)
        {
            if (socket != null)
            {
                socket.Close();
                socket = null;
            }
            bool was = MyId > 0;
            MyId = 0;
            ChatLogged = false;
            HostId = 0;
            Players.Clear();
            Renaming = null;
            if (why.Length > 0) Status = why;
            if (was) RoomChanged?.Invoke();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnLevel;
        void OnDisable() => SceneManager.sceneLoaded -= OnLevel;
        void OnDestroy() { Close(""); if (Instance == this) Instance = null; }

        void OnLevel(Scene s, LoadSceneMode mode)
        {
            if (!Connected) return;
            Send(new LevelMsg { level = s.name, clock = LocalOrbitClock() });
        }

        static double LocalOrbitClock() => OrbitalSystem.Instance != null ? OrbitalSystem.Instance.Time : Time.timeSinceLevelLoad;

        public void Send(object msg)
        {
            if (socket != null && socket.Status == CoopSocket.State.Open) socket.Send(JsonUtility.ToJson(msg));
        }

        void Update()
        {
            if (socket == null) return;
            if (socket.Status == CoopSocket.State.Open && !helloSent)
            {
                helloSent = true;
                Send(new HelloMsg
                {
                    v = Protocol, name = MyName, level = SceneManager.GetActiveScene().name, key = key,
                    // (an AI player says so: other bots know not to follow it)
                    client = CoopBot.Enabled ? CoopBot.ClientName : LaunchArgs.OnWeb ? (SystemInfo.graphicsDeviceType.ToString().Contains("WebGPU") ? "web (WebGPU)" : "web (WebGL)") : Application.platform.ToString(),
                    captain = Captain.Index, clock = LocalOrbitClock(),
                });
                Status = "saying hello...";
            }
            int n = 0;
            while (n++ < 400 && socket != null && socket.Receive(out string text))
            {
                CoopMsg m;
                try { m = JsonUtility.FromJson<CoopMsg>(text); }
                catch { continue; }
                if (m == null || string.IsNullOrEmpty(m.t)) continue;
                Handle(m);
            }
            if (socket != null && socket.Status == CoopSocket.State.Closed)
            {
                // never opened: once more at the page's own address, if there is one to try
                if (!helloSent && fallbackUrl != null)
                {
                    Debug.Log($"[Co-op] {Url} didn't answer ({socket.Error}); trying {fallbackUrl}");
                    socket.Close();
                    Url = fallbackUrl;
                    fallbackUrl = null;
                    Status = $"connecting to {Url}...";
                    socket = CoopSocket.Open(Url);
                    return;
                }
                string err = socket.Error;
                Close(string.IsNullOrEmpty(err) ? (MyId > 0 ? "the server closed the connection" : "couldn't connect") : err);
                Debug.Log($"[Co-op] closed: {Status}");
                return;
            }
            // a name asked for and never answered: a server before renames (it passed the message
            // on to the room instead), given up on quietly
            if (Renaming != null && Time.realtimeSinceStartupAsDouble >= renameUntil)
            {
                Debug.Log($"[Co-op] no answer to the name '{Renaming}': the server can't rename");
                Renaming = null;
                Renamed?.Invoke(false, MyName, "this co-op server is too old to change names");
            }
            // keep the clock in step (more often at first)
            if (Connected && Time.realtimeSinceStartupAsDouble >= nextPing)
            {
                nextPing = Time.realtimeSinceStartupAsDouble + (clock.Synced ? 2.0 : 0.25);
                Send(new PingMsg { c = Time.realtimeSinceStartupAsDouble });
            }
        }

        void Handle(CoopMsg m)
        {
            switch (m.t)
            {
                case CoopWire.Welcome:
                    MyId = m.id;
                    MaxPlayers = m.max;
                    clock.Welcome(m.now, Time.realtimeSinceStartupAsDouble);
                    SetRoom(m.level, m.epoch, m.host);
                    Status = $"connected to {Url}";
                    ChatLogged = m.chatLog;
                    if (ChatLogged) Notices.Post("This co-op server keeps a log of what is said on comms");
                    Debug.Log($"[Co-op] welcome: player #{MyId}, room '{RoomLevel}', host #{HostId}{(ChatLogged ? ", comms logged" : "")}");
                    return;
                case CoopWire.Reject:
                    Close($"refused: {m.why}");
                    return;
                case CoopWire.Room:
                    SetRoom(m.level, m.epoch, m.host);
                    return;
                case CoopWire.Players:
                {
                    Players.Clear();
                    if (m.players != null) Players.AddRange(m.players);
                    if (m.max > 0) MaxPlayers = m.max;
                    // (my name as the server has it: cleaned up, or changed by Rename)
                    var mine = Players.Find(x => x != null && x.id == MyId);
                    if (mine != null && !string.IsNullOrEmpty(mine.name)) MyName = mine.name;
                    // (on to the level too: the other ships' labels follow the names)
                    break;
                }
                case CoopWire.Renamed:
                    // (the answer to Rename: only the server sends it, to the one who asked; one
                    // with a sender, which a server before renames would pass on from a game, or
                    // to a name never asked for, is nobody's answer)
                    if (m.from != 0 || Renaming == null) return;
                    if (m.ok && !string.IsNullOrEmpty(m.name)) MyName = m.name;
                    Debug.Log(m.ok ? $"[Co-op] renamed: {MyName}" : $"[Co-op] the name '{Renaming}' refused: {m.why}");
                    Renaming = null;
                    Renamed?.Invoke(m.ok, MyName, m.ok ? "" : m.why ?? "");
                    return;
                case CoopWire.Name:
                    // (another game's new name, passed on by a server before renames: the
                    // server's roster says who is called what, never another game)
                    return;
                case CoopWire.Pong:
                    clock.Pong(m.c, m.s, Time.realtimeSinceStartupAsDouble);
                    return;
                case CoopWire.Join:
                    if (m.p != null) Notices.Post($"{m.p.name} joined the game");
                    break;
                case CoopWire.Leave:
                {
                    var who = Players.Find(x => x.id == m.id);
                    Notices.Post($"{(who != null ? who.name : "A player")} left the game");
                    break;
                }
            }
            Message?.Invoke(m);
        }

        void SetRoom(string level, double epoch, int host)
        {
            bool changed = host != HostId || level != RoomLevel;
            RoomLevel = level;
            Epoch = epoch;
            HostId = host;
            if (changed)
            {
                Debug.Log($"[Co-op] room '{level}': host #{host}{(host == MyId ? " (me)" : "")}");
                RoomChanged?.Invoke();
            }
        }

        /// <summary>Tell the others which captain is in command here.</summary>
        public void SendCaptain() => Send(new CaptainMsg { captain = Captain.Index });

        /// <summary>Ask the server to call my player something else. The server's answer (Renamed)
        /// says whether it did, and its roster has the name; MyName follows them, never this.
        /// False when not connected.</summary>
        public bool Rename(string name)
        {
            if (!Connected) return false;
            Renaming = (name ?? "").Trim();
            renameUntil = Time.realtimeSinceStartupAsDouble + RenameWait;
            Send(new NameMsg { name = Renaming });
            return true;
        }

        public string NameOf(int id)
        {
            var p = Players.Find(x => x.id == id);
            return p != null ? p.name : $"Player {id}";
        }
    }
}
