using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Co-op on screen. When the game starts it asks: play alone, or join a co-op game (a name,
    /// the server's address and port, remembered for next time)? The same panel is in the pause
    /// menu (Co-op). While connected, a small panel at the top of the screen lists the players,
    /// who is hosting, and the connection's round trip.
    ///
    /// Started with -connect &lt;address:port&gt; [-name &lt;name&gt;] (or in a web page with
    /// ?server=&lt;address:port&gt;&amp;name=...), the game connects at once without asking.
    /// The self-test, the demo reel and the autotest never ask.
    /// </summary>
    public class CoopMenu : MonoBehaviour
    {
        public CarrierDemoInput demo;

        public static bool IsOpen { get; private set; }
        static bool offered;

        string playerName, address, port;
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
            if (CarrierDemoInput.OnWeb)
            {
                var url = Application.absoluteURL;
                string q = Query(url, "server");
                if (q != null) SplitAddress(q, ref address, ref port);
                else if (address.Length == 0) address = HostOf(url);
                playerName = Query(url, "name") ?? playerName;
            }
            // -connect address:port [-name name]: straight in
            string connect = Arg("-connect") ?? (CarrierDemoInput.OnWeb && Query(Application.absoluteURL, "connect") != null ? JoinAddress(address, port) : null);
            if (connect != null)
            {
                SplitAddress(connect, ref address, ref port);
                playerName = Arg("-name") ?? playerName;
                if (!CoopClient.Active && (CoopClient.Instance == null || !CoopClient.Instance.Connecting)) Connect();
                offered = true;
                return;
            }
            if (!offered && !Automated()) Open();
            offered = true;
        }

        static bool Automated()
        {
            string[] modes = { "-systemtest", "-reel", "-autotest", "-webpadtest", "-cooptest" };
            foreach (var m in modes) if (CarrierDemoInput.HasArg(m)) return true;
            if (CarrierDemoInput.OnWeb)
            {
                var url = Application.absoluteURL;
                foreach (var m in new[] { "systemtest", "padtest" }) if (SystemTest.HasQueryKey(url, m)) return true;
            }
            return CarrierDemoInput.ReelMode;
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>A page address's query option: its value, "" for one without a value, or null.</summary>
        static string Query(string url, string key) =>
            SystemTest.HasQueryKey(url, key) ? SystemTest.QueryValue(url, key) ?? "" : null;

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
            if (IsOpen) return;
            IsOpen = true;
            GameMenu.Overlay = true;
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
            IsOpen = false;
            GameMenu.Overlay = false;
            if (paused) { Time.timeScale = savedScale; paused = false; }
        }

        void OnDestroy() { if (IsOpen) { GameMenu.Overlay = false; if (paused) Time.timeScale = savedScale; IsOpen = false; } }

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
            CoopClient.Connect(address, p, playerName);
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
                if ((k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) && !CoopClient.Active) Connect();
            }
            var pad = Gamepad.current;
            if (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) Close();
        }

        bool showingConnected;
        float closeAt;

        void OnGUI()
        {
            if (CoopClient.Active && !IsOpen) Roster();
            if (!IsOpen) return;
            GUI.depth = -200;
            float s = Hud.S;
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.6f));
            float w = 520f * s, h = 380f * s, pad = 22f * s, row = 40f * s;
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
            y += row + 6f * s;

            // what is happening
            var client = CoopClient.Instance;
            string status = client == null ? "<color=#9aa4b0>Not connected</color>"
                : CoopClient.Active ? $"<color=#7ee08a>Connected: {client.Players.Count} of {client.MaxPlayers} players</color>"
                : client.Connecting ? $"<color=#bfe6ff>{client.Status}</color>"
                : $"<color=#ff9070>{client.Status}</color>";
            GUI.Label(new Rect(panel.x + pad, y, w - pad * 2f, 40f * s), status, new GUIStyle(Hud.Text(14, false, TextAnchor.MiddleCenter)) { wordWrap = true });
            y += 46f * s;

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
