using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Co-op on screen. When the game starts it asks: play alone, or join a co-op game (a name,
    /// the server's address and port, and the API key its owner gave you, remembered for next
    /// time: CoopKey)? The same panel is in the pause
    /// menu (Co-op). While connected, a small panel at the top of the screen lists the players,
    /// who is hosting, and the connection's round trip. The panel, opened while connected, lists
    /// the AI wingmen with RENAME: the new name goes out as an order on the comms ("Wingman, your
    /// name is Ace"), which the wingman takes from its leader or the host.
    ///
    /// Started with -connect &lt;address:port&gt; [-name &lt;name&gt;] (or in a web page with
    /// ?server=&lt;address:port&gt;&amp;name=...), the game connects at once without asking.
    /// The self-test, the demo reel and the autotest never ask.
    /// </summary>
    public class CoopMenu : MonoBehaviour
    {
        public CarrierDemoInput demo;

        public static bool IsOpen => InputFocus.Has(InputFocus.Panel.CoopPanel);
        static bool offered;

        string playerName, address, port, apiKey, keyName;
        // the key as it was loaded for keyName (one typed or pasted since is never replaced)
        string loadedKey = "";
        bool paused;
        float savedScale = 1f;
        float openedAt;
        Rect panel;

        const string PrefName = "coop.name", PrefAddress = "coop.address", PrefPort = "coop.port";

        void Start()
        {
            playerName = Pref(PrefName, System.Environment.UserName);
            address = Pref(PrefAddress, "");
            port = Pref(PrefPort, "7777");
            // a web page: its own server by default, or ?server=address:port
            if (LaunchArgs.OnWeb)
            {
                string q = LaunchArgs.PageOption("server");
                if (q != null) SplitAddress(q, ref address, ref port);
                else if (address.Length == 0) address = HostOf(LaunchArgs.PageUrl);
                playerName = LaunchArgs.PageOption("name") ?? playerName;
            }
            LoadKey();
            // -connect address:port [-name name]: straight in
            string connect = LaunchArgs.ValueOf("-connect") ?? (LaunchArgs.PageHas("connect") ? JoinAddress(address, port) : null);
            if (connect != null)
            {
                SplitAddress(connect, ref address, ref port);
                // (an AI wingman: the name it was last given wins over -name)
                playerName = (CoopBot.Enabled ? CoopBot.KeptName : null) ?? LaunchArgs.ValueOf("-name") ?? playerName;
                LoadKey();
                if (!CoopClient.Active && (CoopClient.Instance == null || !CoopClient.Instance.Connecting)) Connect();
                offered = true;
                return;
            }
            // the start menu comes first, and offers this once it is done
            if (StartMenu.WillOpen) return;
            Offer();
        }

        /// <summary>The key kept for the name in the panel (in the web version each name has its own).</summary>
        void LoadKey()
        {
            apiKey = loadedKey = CoopKey.For(playerName);
            keyName = playerName;
        }

        /// <summary>Ask once a run whether to play with others (not in tests or reels).</summary>
        public void Offer()
        {
            if (!offered && !LaunchArgs.Automated && !Hud.ReelMode) Open();
            offered = true;
        }

        static string HostOf(string url)
        {
            try { return new System.Uri(url).Host; } catch { return ""; }
        }

        /// <summary>An address and port as one "address:port", the way SplitAddress takes it apart:
        /// an IPv6 address in brackets, a full ws:// or wss:// address as it is.</summary>
        internal static string JoinAddress(string address, string port)
        {
            address = (address ?? "").Trim();
            if (address.StartsWith("ws://") || address.StartsWith("wss://")) return address;
            return address.Contains(":") && !address.StartsWith("[") ? $"[{address}]:{port}" : $"{address}:{port}";
        }

        /// <summary>"address:port" into its parts (the port is left as it was when none is given).</summary>
        internal static void SplitAddress(string s, ref string address, ref string port)
        {
            s = s.Trim();
            if (s.StartsWith("ws://") || s.StartsWith("wss://")) { address = s; return; }
            int colon = s.LastIndexOf(':');
            // an IPv6 address has colons of its own: with a port it is in brackets, [::1]:7777;
            // without brackets (::1, fe80::2) all of it is the address
            bool bareIPv6 = s.IndexOf(':') != colon && s.IndexOf('[') < 0;
            if (colon > 0 && !bareIPv6 && s.IndexOf(']') < colon && int.TryParse(s.Substring(colon + 1), out _))
            {
                address = s.Substring(0, colon).Trim('[', ']');
                port = s.Substring(colon + 1);
            }
            else address = s;
        }

        static string Pref(string key, string fallback)
        {
            try { return PlayerPrefs.GetString(key, fallback); } catch { return fallback; }
        }

        public void Open()
        {
            if (IsOpen || !InputFocus.Take(InputFocus.Panel.CoopPanel)) return;
            openedAt = Time.unscaledTime;
            // alone, the game waits while you choose; with others, it can't
            if (!CoopClient.Active)
            {
                paused = true;
                savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            InputFocus.Release(InputFocus.Panel.CoopPanel);
            if (paused) { Time.timeScale = savedScale; paused = false; }
        }

        void OnDestroy() { if (IsOpen) { InputFocus.Release(InputFocus.Panel.CoopPanel); if (paused) Time.timeScale = savedScale; } }

        void Connect()
        {
            int p = int.TryParse(port, out int v) && v > 0 && v < 65536 ? v : 7777;
            try
            {
                PlayerPrefs.SetString(PrefName, playerName);
                PlayerPrefs.SetString(PrefAddress, address);
                PlayerPrefs.SetString(PrefPort, p.ToString());
                PlayerPrefs.Save();
            }
            catch { }
            CoopKey.Keep(playerName, apiKey);
            CoopClient.Connect(address, p, playerName, apiKey);
        }

        void Update()
        {
            if (!IsOpen) return;
            // connected: off it goes
            if (CoopClient.Active && Time.unscaledTime - openedAt > 0.2f && !showingConnected) { showingConnected = true; closeAt = Time.unscaledTime + 1.2f; }
            if (showingConnected && Time.unscaledTime >= closeAt) { showingConnected = false; Close(); return; }
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame) { Close(); return; }
                bool enter = k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
                if (enter && !CoopClient.Active) Connect();
                else if (enter && renameId != 0) SendRename();
            }
            var pad = Gamepad.current;
            if (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) Close();
        }

        bool showingConnected;
        float closeAt;

        // the AI wingman being renamed (0: none), the name it had when RENAME was pressed, the new
        // name being typed, and a line about the last one (a name refused, or the order sent)
        int renameId;
        string renameOld = "", newName = "", renameNote = "";

        /// <summary>The AI wingmen in the game (not me): RENAME is offered for each.</summary>
        static List<CoopPlayer> Wingmen(CoopClient c) =>
            c == null || !CoopClient.Active ? new List<CoopPlayer>()
                : c.Players.Where(p => p != null && p.id != c.MyId && (p.ai || CoopBot.IsBot(p))).ToList();

        /// <summary>The new name, sent as an order on the comms ("Wingman, your name is Ace"), so
        /// the wingman's own rule says who may rename it (its leader or the host). A name no one
        /// may have, or another player's, is refused here.</summary>
        void SendRename()
        {
            var c = CoopClient.Instance;
            string n = (newName ?? "").Trim();
            string why = CoopWire.NameRefused(n);
            if (why.Length == 0 && c != null && c.Players.Any(x => x != null && x.id != renameId && string.Equals((x.name ?? "").Trim(), n, System.StringComparison.OrdinalIgnoreCase)))
                why = $"another player is called {n}";
            if (why.Length > 0) { renameNote = $"<color=#ff9070>Not {(n.Length > 0 ? n : "that")}: {why}</color>"; return; }
            if (n != renameOld)
                renameNote = CoopChat.Say($"{renameOld}, your name is {n}")
                    ? $"Asked {renameOld} to take the name {n}: the answer is on the comms"
                    : "<color=#ff9070>Not sent: not connected</color>";
            renameId = 0;
        }

        void OnGUI()
        {
            if (CoopClient.Active && !IsOpen) Roster();
            if (!IsOpen) return;
            GUI.depth = -200;
            float s = Hud.S;
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.6f));
            float w = 520f * s, h = 440f * s, pad = 22f * s, row = 40f * s;
            // (connected: the AI wingmen, each with RENAME, above the buttons)
            var wingmen = Wingmen(CoopClient.Instance);
            if (!wingmen.Any(p => p.id == renameId)) renameId = 0;
            if (wingmen.Count > 0) h += 30f * s + wingmen.Count * row + (renameNote.Length > 0 ? 20f * s : 0f);
            panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Hud.Fill(panel, new Color(0.03f, 0.05f, 0.1f, 0.96f));
            Hud.Fill(new Rect(panel.x, panel.y, w, Mathf.Max(2f, 3f * s)), new Color(1f, 0.72f, 0.3f));
            float y = panel.y + pad;
            GUI.Label(new Rect(panel.x, y, w, 32f * s), "CO-OP", Hud.Text(26, true, TextAnchor.MiddleCenter));
            y += 34f * s;
            GUI.Label(new Rect(panel.x, y, w, 22f * s), "Play alone, or join friends on a co-op server", Hud.Text(15, false, TextAnchor.MiddleCenter));
            y += 34f * s;

            var field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(17 * s), alignment = TextAnchor.MiddleLeft };
            float lw = 150f * s, fx = panel.x + pad + lw, fw = w - pad * 2f - lw;
            GUI.Label(new Rect(panel.x + pad, y, lw, row - 8f * s), "Your name", Hud.Text(16, false, TextAnchor.MiddleLeft));
            playerName = GUI.TextField(new Rect(fx, y, fw, row - 8f * s), playerName ?? "", 24, field);
            y += row;
            GUI.Label(new Rect(panel.x + pad, y, lw, row - 8f * s), "Server address", Hud.Text(16, false, TextAnchor.MiddleLeft));
            address = GUI.TextField(new Rect(fx, y, fw, row - 8f * s), address ?? "", 120, field);
            y += row;
            GUI.Label(new Rect(panel.x + pad, y, lw, row - 8f * s), "Port", Hud.Text(16, false, TextAnchor.MiddleLeft));
            port = GUI.TextField(new Rect(fx, y, 110f * s, row - 8f * s), port ?? "7777", 5, field);
            y += row;
            // (the web version keeps a key for each name: another name, its key, unless a key has
            // been typed or pasted in since, which is kept whatever the name)
            if (CoopKey.PerName && playerName != keyName)
            {
                if (apiKey == loadedKey) LoadKey();
                else keyName = playerName;
            }
            GUI.Label(new Rect(panel.x + pad, y, lw, row - 8f * s), "API key", Hud.Text(16, false, TextAnchor.MiddleLeft));
            apiKey = GUI.PasswordField(new Rect(fx, y, fw, row - 8f * s), apiKey ?? "", '•', 80, field);
            y += row - 8f * s;
            string kept = CoopKey.Masked(apiKey);
            GUI.Label(new Rect(fx, y, fw, 18f * s), (kept.Length > 0 ? kept + "   " : "") +
                      (CoopKey.PerName ? "From the server's owner. Kept for this name, in this browser" : "From the server's owner. Kept for next time"),
                      Hud.Text(11, false, TextAnchor.MiddleLeft));
            y += 26f * s;

            // what is happening
            var client = CoopClient.Instance;
            string status = client == null ? "<color=#9aa4b0>Not connected</color>"
                : CoopClient.Active ? $"<color=#7ee08a>Connected: {client.Players.Count} of {client.MaxPlayers} players</color>"
                : client.Connecting ? $"<color=#bfe6ff>{client.Status}</color>"
                : $"<color=#ff9070>{client.Status}</color>";
            GUI.Label(new Rect(panel.x + pad, y, w - pad * 2f, 40f * s), status, new GUIStyle(Hud.Text(14, false, TextAnchor.MiddleCenter)) { wordWrap = true });
            y += 46f * s;

            // the AI wingmen: RENAME takes a new name and sends it as an order on the comms
            if (wingmen.Count > 0)
            {
                GUI.Label(new Rect(panel.x + pad, y, w - pad * 2f, 24f * s), "AI WINGMEN", Hud.Text(14, true, TextAnchor.MiddleLeft));
                y += 30f * s;
                var small = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Bold };
                float rh = row - 8f * s, rbw = 96f * s;
                foreach (var p in wingmen)
                {
                    float x = panel.x + pad, rx = panel.xMax - pad;
                    if (p.id == renameId)
                    {
                        // (the new name, then OK and cancel)
                        GUI.Label(new Rect(x, y, lw, rh), $"{p.name} to", Hud.Text(16, false, TextAnchor.MiddleLeft));
                        float ok = 72f * s, gap = 6f * s;
                        newName = GUI.TextField(new Rect(fx, y, fw - (ok + gap) * 2f, rh), newName ?? "", 24, field);
                        if (GUI.Button(new Rect(rx - ok * 2f - gap, y, ok, rh), "OK", small)) SendRename();
                        if (GUI.Button(new Rect(rx - ok, y, ok, rh), "Cancel", small)) { renameId = 0; renameNote = ""; }
                    }
                    else
                    {
                        GUI.Label(new Rect(x, y, w - pad * 2f - rbw, rh), p.name, Hud.Text(16, false, TextAnchor.MiddleLeft));
                        if (GUI.Button(new Rect(rx - rbw, y, rbw, rh), "RENAME", small))
                        {
                            renameId = p.id;
                            renameOld = p.name;
                            newName = p.name;
                            renameNote = "";
                        }
                    }
                    y += row;
                }
                if (renameNote.Length > 0)
                {
                    GUI.Label(new Rect(panel.x + pad, y - 6f * s, w - pad * 2f, 20f * s), renameNote, Hud.Text(12, false, TextAnchor.MiddleLeft));
                    y += 20f * s;
                }
            }

            // the buttons
            var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold };
            float bw = (w - pad * 3f) * 0.5f, bh = 44f * s;
            bool connected = CoopClient.Active;
            if (GUI.Button(new Rect(panel.x + pad, y, bw, bh), connected ? "Back to the game" : "Play solo", button)) Close();
            string right = connected ? "Disconnect" : client != null && client.Connecting ? "Cancel" : "Connect";
            if (GUI.Button(new Rect(panel.x + pad * 2f + bw, y, bw, bh), right, button))
            {
                if (connected || (client != null && client.Connecting)) CoopClient.Disconnect();
                else Connect();
            }
            GUI.Label(new Rect(panel.x, panel.yMax - pad - 16f * s, w, 18f * s), "Enter connect   Esc play solo", Hud.Text(12, false, TextAnchor.MiddleCenter));
        }

        /// <summary>Who is playing, at the top of the screen.</summary>
        void Roster()
        {
            var c = CoopClient.Instance;
            float s = Hud.S;
            var st = Hud.Text(13);
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>CO-OP</b>  <color=#9aa4b0>{c.Players.Count}/{c.MaxPlayers} players   {c.Rtt * 1000.0:F0} ms</color>");
            int scaledFor = CoopWorld.Instance != null ? CoopWorld.Instance.ScaledFor : 1;
            if (scaledFor > 1) sb.Append($"\n<color=#ff9a6a>Enemies x{CoopScaling.Toughness(scaledFor):0.#} tough, x{CoopScaling.Firepower(scaledFor):0.##} firepower</color>");
            foreach (var p in c.Players)
            {
                bool here = p.level == c.RoomLevel;
                string tag = p.id == c.HostId && here ? " <color=#ffc070>host</color>" : "";
                string where = here ? "" : $" <color=#9aa4b0>({p.level})</color>";
                string who = p.id == c.MyId ? $"<color=#7ee08a>{p.name}</color>" : p.name;
                sb.Append($"\n{who}{tag}{where}");
            }
            var content = new GUIContent(sb.ToString());
            var size = st.CalcSize(content);
            float pad = 8f * s;
            var r = new Rect((Screen.width - size.x) * 0.5f - pad, pad, size.x + pad * 2f, size.y + pad * 2f);
            Hud.Panel(r);
            GUI.Label(new Rect(r.x + pad, r.y + pad, size.x, size.y), content, st);
        }
    }
}
