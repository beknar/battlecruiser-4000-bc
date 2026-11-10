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
        public const int Protocol = 1;
        public static CoopClient Instance { get; private set; }

        /// <summary>Connected, welcomed, and playing together.</summary>
        public static bool Active => Instance != null && Instance.Connected;

        public bool Connected => socket != null && socket.Status == CoopSocket.State.Open && MyId > 0;
        public bool Connecting => socket != null && socket.Status != CoopSocket.State.Closed && MyId == 0;
        public string Url { get; private set; } = "";
        /// <summary>What is going on, in a few words (for the menus).</summary>
        public string Status { get; private set; } = "";
        public int MyId { get; private set; }
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

        /// <summary>Raised for every game message (not the server's own ones), and when the
        /// room changes host.</summary>
        public static event Action<CoopMsg> Message;
        public static event Action RoomChanged;

        CoopSocket socket;
        readonly ClockSync clock = new ClockSync();
        double nextPing;
        bool helloSent;

        /// <summary>Connect (closing any connection there is): address as a host name or IP
        /// (or a full ws:// or wss:// address), and the port.</summary>
        public static void Connect(string address, int port, string name)
        {
            if (Instance == null)
            {
                var go = new GameObject("Co-op connection");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<CoopClient>();
            }
            Instance.Open(address, port, name);
        }

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

        public static void Disconnect()
        {
            if (Instance != null) Instance.Close("disconnected");
        }

        void Open(string address, int port, string name)
        {
            Close("");
            address = (address ?? "").Trim();
            MyName = string.IsNullOrWhiteSpace(name) ? "Captain" : name.Trim();
            // a page served over https may only open wss:// sockets
            bool secure = CarrierDemoInput.OnWeb && Application.absoluteURL.StartsWith("https:");
            string url = UrlFor(address, port, secure);
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
            HostId = 0;
            Players.Clear();
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
                    v = Protocol, name = MyName, level = SceneManager.GetActiveScene().name,
                    client = CarrierDemoInput.OnWeb ? (SystemInfo.graphicsDeviceType.ToString().Contains("WebGPU") ? "web (WebGPU)" : "web (WebGL)") : Application.platform.ToString(),
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
                string err = socket.Error;
                Close(string.IsNullOrEmpty(err) ? (MyId > 0 ? "the server closed the connection" : "couldn't connect") : err);
                Debug.Log($"[Co-op] closed: {Status}");
                return;
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
                case "welcome":
                    MyId = m.id;
                    MaxPlayers = m.max;
                    clock.Welcome(m.now, Time.realtimeSinceStartupAsDouble);
                    SetRoom(m.level, m.epoch, m.host);
                    Status = $"connected to {Url}";
                    Debug.Log($"[Co-op] welcome: player #{MyId}, room '{RoomLevel}', host #{HostId}");
                    return;
                case "reject":
                    Close($"refused: {m.why}");
                    return;
                case "room":
                    SetRoom(m.level, m.epoch, m.host);
                    return;
                case "players":
                    Players.Clear();
                    if (m.players != null) Players.AddRange(m.players);
                    if (m.max > 0) MaxPlayers = m.max;
                    return;
                case "pong":
                    clock.Pong(m.c, m.s, Time.realtimeSinceStartupAsDouble);
                    return;
                case "join":
                    if (m.p != null) CarrierDemoInput.Toast($"{m.p.name} joined the game");
                    break;
                case "leave":
                {
                    var who = Players.Find(x => x.id == m.id);
                    CarrierDemoInput.Toast($"{(who != null ? who.name : "A player")} left the game");
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

        public string NameOf(int id)
        {
            var p = Players.Find(x => x.id == id);
            return p != null ? p.name : $"Player {id}";
        }
    }
}
