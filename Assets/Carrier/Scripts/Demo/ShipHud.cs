using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The HUD around the player's ship: it puts up the panels (the target view, the mining
    /// marks and cargo list, the console and the captain on it) and draws the controls box in the
    /// top-left corner (whichever set the player is using, leaving out systems this ship doesn't
    /// have), the banner while the ship is lost, and each message posted to Notices.
    /// </summary>
    public partial class ShipHud : MonoBehaviour
    {
        public CarrierDemoInput demo;
        bool wasPainted;

        /// <summary>The HUD's EMP lines for a ship: a burst charging in reach, and its components
        /// an EMP has out ("" none).</summary>
        public static string EmpText(CarrierController ship)
        {
            if (ship == null) return "";
            var sb = new System.Text.StringBuilder(EmpBurst.HudText(ship.transform));
            var sys = ship.GetComponent<ShipSystems>();
            if (sys != null)
                foreach (var c in ShipSystems.Components)
                {
                    float left = sys.KnockedFor(c);
                    if (left <= 0f) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"EMP: {ShipSystems.Label(c)} back in {left:0} s");
                }
            return sb.ToString();
        }

        TargetDisplay display;
        static Rect helpRect;
        bool showHelp = true;

        /// <summary>Screen area of the controls box (GUI coordinates).</summary>
        public static Rect HelpRect => helpRect;

        CarrierController Carrier => demo != null ? demo.carrier : null;

        /// <summary>Put up the HUD for `demo`'s ship, on its object. This comes first of the panels,
        /// so the controls box is laid out (and drawn) before the panels that keep clear of it.</summary>
        public static ShipHud Build(CarrierDemoInput demo)
        {
            var go = demo.gameObject;
            var hud = go.AddComponent<ShipHud>();
            hud.demo = demo;
            hud.display = go.AddComponent<TargetDisplay>();
            hud.display.carrier = demo.carrier;
            go.AddComponent<MiningDisplay>().carrier = demo.carrier;
            // the console in the bottom-left corner, and the captain on it
            go.AddComponent<CaptainPortrait>().demo = demo;
            go.AddComponent<ShipConsole>().demo = demo;
            // the modes of play: their menu, and the combat mode's
            go.AddComponent<ModeMenu>();
            go.AddComponent<AutoFly>().demo = demo;
            go.AddComponent<Landmarks>();
            go.AddComponent<SpawnDirector>();
            go.AddComponent<CombatMode>().demo = demo;
            go.AddComponent<Salvage>().demo = demo;
            go.AddComponent<MiningMode>().demo = demo;
            go.AddComponent<NavigationMode>().demo = demo;
            go.AddComponent<FleetDirector>().demo = demo;
            return hud;
        }

        void OnEnable() => Notices.Posted += Show;
        void OnDisable() => Notices.Posted -= Show;

        public void ToggleHelp() => showHelp = !showHelp;

        /// <summary>The controls box's text for the keyboard or the pad (for the self-test).</summary>
        public string HelpText(bool pad) => Carrier == null ? "" : pad ? PadHelp : KeyHelp;

        /// <summary>A panel covers this point (GUI coordinates): a click there isn't on a ship.</summary>
        public bool Covers(Vector2 gui) =>
            helpRect.Contains(gui) || ShipConsole.Rect.Contains(gui) || (display != null && display.PanelRect.Contains(gui)) || MiningDisplay.ManifestRect.Contains(gui)
            || ModeMenu.Area.Contains(gui) || FleetDirector.PanelsRect.Contains(gui) || CoopChat.Area.Contains(gui);

        // ------------------------------------------------------------ messages

        string toast;
        float toastUntil;
        // a reel runs on game time (locked to its frame rate), so its messages last as long on film as in play
        static float Clock => Hud.ReelMode ? Time.time : Time.unscaledTime;

        void Show(string message)
        {
            toast = message;
            // (long enough to read: a long one, a comms message, stays up longer, 10 s at most)
            toastUntil = Clock + Mathf.Clamp(message != null ? message.Length / 30f : 0f, 2.5f, 10f);
        }

        // ------------------------------------------------------------ the controls box

        string keyHelp, padHelp;
        // (the ship and the mode the lists were made for: another ship, taken command of, has
        // other systems; another mode, other keys)
        object helpFor;
        ModeOfPlay helpMode;
        int helpFleet;

        void HelpFor(CarrierController carrier)
        {
            int fleet = FleetDirector.Instance != null ? FleetDirector.Instance.Ships.Count : 0;
            if (ReferenceEquals(helpFor, carrier) && helpMode == Modes.Current && helpFleet == fleet) return;
            helpFor = carrier;
            helpMode = Modes.Current;
            helpFleet = fleet;
            keyHelp = padHelp = null;
            keyRows = padRows = null;
        }

        static bool OnWeb => LaunchArgs.OnWeb;
        /// <summary>The text for here, or for a browser: it has no system fonts to fall back on,
        /// and the built-in font has no arrows.</summary>
        static string W(string native, string web) => OnWeb ? web : native;

        static string modeBar;
        static ModeOfPlay modeBarFor;
        static bool modeBarPad;

        /// <summary>The modes, the one the player is in picked out (made again only when the mode
        /// or the controls in use change).</summary>
        static string ModeBar(bool pad)
        {
            if (modeBar != null && modeBarFor == Modes.Current && modeBarPad == pad) return modeBar;
            modeBarFor = Modes.Current;
            modeBarPad = pad;
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b><color=#ffc070>{Modes.Name(Modes.Current)} MODE</color></b>   ");
            foreach (var m in Modes.All)
            {
                string name = Modes.Name(m).Substring(0, 1) + Modes.Name(m).Substring(1).ToLowerInvariant();
                string item = pad ? name : $"{Modes.Number(m)} {name}";
                sb.Append(m == Modes.Current ? $"<b>{item}</b>  " : $"<color=#8a95a3>{item}</color>  ");
            }
            return modeBar = sb.ToString().TrimEnd();
        }

        System.Collections.Generic.List<HelpRow> keyRows, padRows;

        /// <summary>The controls box's text, a line a row (for the self-test and the logs).</summary>
        // (the cache checked first: another mode, ship or fleet since, even with the box hidden)
        string KeyHelp { get { HelpFor(Carrier); return keyHelp ??= Flatten(ModeBar(false), KeyRows); } }
        string PadHelp { get { HelpFor(Carrier); return padHelp ??= Flatten(ModeBar(true), PadRows); } }

        /// <summary>The controls for the mode the player is in, leaving out systems this ship doesn't
        /// have, then the keys that are the same in every mode: a key and what it does, a row each.</summary>
        System.Collections.Generic.List<HelpRow> KeyRows
        {
            get
            {
                var carrier = Carrier;
                HelpFor(carrier);
                if (keyRows != null) return keyRows;
                var r = new System.Collections.Generic.List<HelpRow>();
                void K(string key, string what) => r.Add(new HelpRow(key, what));
                r.Add(HelpRow.Heading("THIS MODE"));
                switch (Modes.Current)
                {
                    case ModeOfPlay.Combat:
                        K("F", "fire guns");
                        K("G", "auto-fire on/off");
                        if (carrier.HasMissiles) K("R", "missile salvo");
                        if (demo.MainGun != null) K("B", "main gun: charge (fires after 10 s)");
                        K("T / Shift+T", "next / previous target");
                        K("Click", "pick a ship");
                        K("J", "aim at a part");
                        K("X", "stop targeting");
                        K("C", "line up");
                        K("U", "fly to the target");
                        if (KineticStrike.Instance != null) K("Y", "kinetic strike from orbit");
                        if (demo.Cargo != null) { K("Z", "salvage a wreck (flies to it)"); K("Shift+Z", "next wreck"); }
                        if (FleetDirector.Instance != null && FleetDirector.Instance.Ships.Count > 0) { K("V", "the fleet attacks my target"); K("Shift+V", "the fleet forms up"); }
                        K("Tab", "the targets and wrecks near");
                        break;
                    case ModeOfPlay.Mining:
                        if (demo.Laser == null) { r.Add(HelpRow.Note("No mining gear on this ship")); break; }
                        K("Z", "mining laser on/off");
                        K("V", "mineral scan");
                        K("T / Shift+T", "next / previous deposit (in the list's order)");
                        K("O", "list the deposits nearest, most valuable or richest first");
                        K("F", "list the deposits with one mineral in them (or all)");
                        K("C", "fly to it");
                        K("I", "cargo hold");
                        K("G", "co-op, on ore in the Tab list: give it to an AI wingman within 50 km");
                        K("J", "jettison cheapest ore");
                        K("Tab", "the deposits found, and the ore aboard (the wheel chooses in it)");
                        break;
                    case ModeOfPlay.Navigation:
                        if (demo.Warp != null) K("M", "system map: warp to a planet, moon, field...");
                        K("Tab", "the points of interest here: Enter flies there, K lands or docks there, C marks the course");
                        K("X", "stop the auto-travel (or just steer)");
                        if (PlanetFlight.Instance != null) K("O", "cruise on/off (the higher, the faster)");
                        break;
                    case ModeOfPlay.Services:
                        K("Tab or U", "the station's or base's services, docked or landed on its pad: storage, refining, building, fitting, trade, crew, the shipyard (what the place offers); Tab, U or Esc closes it, Q / E turn its pages");
                        break;
                    case ModeOfPlay.Fleet:
                        if (FleetDirector.Instance == null || FleetDirector.Instance.Ships.Count == 0) { r.Add(HelpRow.Note("No fleet yet: build a ship at a shipyard, and have her join you there (services)")); break; }
                        K("F", "follow me");
                        K("G", "guard me");
                        K("R", "form up");
                        K("V", "attack my target");
                        K("Z", "hold position");
                        K("B", "view the next ship (and back)");
                        K("Tab", "the fleet: choose who gets the orders");
                        break;
                }
                r.Add(HelpRow.Heading("EVERY MODE"));
                K("W / S", "throttle");
                K("A / D", "turn");
                K("Q / E", "roll");
                K(W("\u2191 / \u2193", "Up / Down"), "nose up / down");
                K("L", "level off");
                K("Space / Ctrl", "elevator thrusters up / down");
                K("Right-drag", "orbit the camera");
                K("Wheel", "zoom the camera");
                if (demo.Warp != null && Modes.Current != ModeOfPlay.Navigation) K("M", "system map");
                if (PlanetLanding.Instance != null) K("K", Docking.Instance != null ? "land / take off, dock / undock" : "land / take off (in orbit)");
                K("H", "hide help");
                K("+ / -", "text size");
                K("N", "sound");
                K("Shift+N", "next song");
                K("F11", OnWeb ? "full screen" : "window");
                K("Esc or P", "menu");
                return keyRows = r;
            }
        }

        System.Collections.Generic.List<HelpRow> PadRows
        {
            get
            {
                var carrier = Carrier;
                HelpFor(carrier);
                if (padRows != null) return padRows;
                var r = new System.Collections.Generic.List<HelpRow>();
                void K(string key, string what) => r.Add(new HelpRow(key, what));
                r.Add(HelpRow.Heading("THIS MODE"));
                K("Back+LB/RB", "mode");
                K("Back+Y", "this mode's menu");
                switch (Modes.Current)
                {
                    case ModeOfPlay.Combat:
                        K("RB", "fire guns");
                        if (carrier.HasMissiles) K("LB", "missile salvo");
                        K("L3", "auto-fire");
                        if (demo.MainGun != null) K("X", "main gun: charge");
                        K("Y", "next target");
                        K("B", "stop targeting");
                        K("Back+L3", "aim at a part");
                        K("A", "line up on the target");
                        if (demo.Cargo != null) K("Back+X", "salvage");
                        break;
                    case ModeOfPlay.Mining:
                        if (demo.Laser == null) { r.Add(HelpRow.Note("No mining gear on this ship")); break; }
                        K("A", "mining laser on/off");
                        K("Y", "mineral scan");
                        K("X", "fly to the chosen deposit");
                        K("RB", "cargo hold");
                        K("B", "jettison cheapest ore");
                        break;
                    case ModeOfPlay.Navigation:
                        if (demo.Warp != null) K("R3", "system map: warp");
                        K("Back+Y", "the points of interest here");
                        K("A", "fly to the course");
                        K("LB", "fly there and land or dock");
                        K("B", "stop the auto-travel");
                        K("X", "cruise on/off");
                        break;
                    case ModeOfPlay.Services:
                        K("A", "the station's or base's services");
                        break;
                    case ModeOfPlay.Fleet:
                        if (FleetDirector.Instance == null || FleetDirector.Instance.Ships.Count == 0) { r.Add(HelpRow.Note("No fleet yet: build a ship at a shipyard")); break; }
                        K("A", "follow me");
                        K("X", "guard me");
                        K("Y", "form up");
                        K("RB", "attack my target");
                        K("B", "hold");
                        K("LB", "view a ship");
                        break;
                }
                r.Add(HelpRow.Heading("EVERY MODE"));
                K("Left stick", "throttle / turn");
                K(W("D-pad \u2191/\u2193", "D-pad up/down"), "nose");
                K(W("D-pad \u2190/\u2192", "D-pad left/right"), "roll");
                K("Back+A", "level off");
                K("LT / RT", "elevator thrusters down / up");
                K("Right stick", "orbit camera");
                K(W("Back+D-pad \u2190/\u2192", "Back+D-pad left/right"), "zoom");
                if (demo.Warp != null && Modes.Current != ModeOfPlay.Navigation) K("R3", "system map");
                if (PlanetLanding.Instance != null) K("Back+B", Docking.Instance != null ? "land / take off, dock / undock" : "land / take off");
                K("Back", "hide help");
                K("Start", "menu");
                K(W("Back+D-pad \u2191/\u2193", "Back+D-pad up/down"), "text size");
                return padRows = r;
            }
        }

        // ------------------------------------------------------------ drawing

        void OnGUI()
        {
            var carrier = Carrier;
            if (carrier == null || Hud.ReelNoShip) return;
            float s = Hud.S;
            bool pad = PadSupport.UsingPad && Gamepad.current != null;
            float pad2 = 12f * s;
            helpRect = Rect.zero;
            if (!Hud.ReelMode)
            {
                // the controls (the ship's state is on the console, bottom left)
                if (showHelp) helpRect = DrawHelp(pad ? PadRows : KeyRows, ModeBar(pad), s);
                else
                {
                    var style = Hud.Text(16);
                    var content = new GUIContent(ModeBar(pad) + (pad ? "\n<i>Back: show controls</i>" : "\n<i>H show controls</i>"));
                    Vector2 sz = style.CalcSize(content);
                    helpRect = new Rect(pad2, pad2, sz.x + pad2 * 2f, sz.y + pad2 * 2f);
                    Hud.Panel(helpRect);
                    GUI.Label(new Rect(helpRect.x + pad2, helpRect.y + pad2, sz.x, sz.y), content, style);
                }
            }

            if (carrier.Destroyed)
            {
                var big = Hud.Text(40, true, TextAnchor.MiddleCenter);
                var msg = carrier.Captured
                    ? new GUIContent($"<color=#ff6050>{carrier.shipName.ToUpper()} CAPTURED BY THE {carrier.CapturedBy.ToUpper()}</color>\n" +
                                     $"<size={Mathf.RoundToInt(22 * s)}>{carrier.LastTaken} of the crew taken prisoner; " +
                                     $"the survivors bring her back to the last warp point in {Mathf.CeilToInt(carrier.RespawnIn)} s</size>")
                    : new GUIContent($"<color=#ff6050>{carrier.shipName.ToUpper()} DESTROYED</color>\n" +
                                     $"<size={Mathf.RoundToInt(22 * s)}>respawning in {Mathf.CeilToInt(carrier.RespawnIn)} s</size>");
                Vector2 bsz = big.CalcSize(msg);
                var br = new Rect((Screen.width - bsz.x) * 0.5f - pad2 * 2f, Screen.height * 0.28f, bsz.x + pad2 * 4f, bsz.y + pad2 * 2f);
                Hud.Panel(br);
                GUI.Label(br, msg, big);
            }

            // the warnings, stacked from the top, each below the last (none hides another):
            // jamming, a targeting laser, an EMP, a tractor lock, boarders
            float wy = Screen.height * 0.045f, gap = 4f * s;
            Rect Warn(GUIStyle st, GUIContent gc, float atLeast, float extra)
            {
                Vector2 sz = st.CalcSize(gc);
                var r = new Rect((Screen.width - sz.x) * 0.5f - pad2 * 2f, Mathf.Max(wy, atLeast), sz.x + pad2 * 4f, sz.y + extra);
                Hud.Panel(r);
                GUI.Label(r, gc, st);
                wy = r.yMax + gap;
                return r;
            }

            // jammed: how much (ECCM already taken off)
            float jammed = carrier.Destroyed ? 0f : Jamming.On(carrier.transform);
            if (jammed > 0.05f)
                Warn(Hud.Text(16, true, TextAnchor.MiddleCenter),
                     new GUIContent($"<color=#c080ff>JAMMED {jammed:P0}</color>  <size={Mathf.RoundToInt(13 * s)}>sensors, aim, missiles and comms degraded</size>"), 0f, pad2);

            // painted by a targeting laser: who, and from where (a tone as it starts)
            string painted = carrier.Destroyed ? "" : Designation.HudText(carrier.transform);
            if (painted.Length > 0 && !wasPainted) SoundFX.Ui(Sfx.UiAlert);
            wasPainted = painted.Length > 0;
            if (painted.Length > 0)
            {
                int cut = painted.IndexOf('\n');
                Warn(Hud.Text(18, true, TextAnchor.MiddleCenter),
                     new GUIContent($"<color=#ffb040>{painted.Substring(0, cut)}</color>\n<size={Mathf.RoundToInt(14 * s)}>{painted.Substring(cut + 1)}</size>"), 0f, pad2 * 2f);
            }

            // an EMP: a burst charging with this ship in reach (time to get out), and what an EMP
            // has knocked out, with the seconds till it is back
            string emp = carrier.Destroyed ? "" : EmpText(carrier);
            if (emp.Length > 0)
                Warn(Hud.Text(17, true, TextAnchor.MiddleCenter), new GUIContent($"<color=#80c0ff>{emp}</color>"), 0f, pad2);

            // held by tractor beams: how many, and whether full thrust can break free
            if (!carrier.Destroyed && carrier.Tractored)
            {
                int n = carrier.TractorBeams;
                Warn(Hud.Text(24, true, TextAnchor.MiddleCenter),
                     new GUIContent($"<color=#ff6050>Tractor lock: {n} beam{(n == 1 ? "" : "s")}</color>\n" +
                                    $"<size={Mathf.RoundToInt(16 * s)}>{(carrier.TractorFirm ? "held fast: no warp, docking or landing" : "full thrust away to break free")}</size>"),
                     Screen.height * 0.16f, pad2 * 2f);
            }

            // boarders aboard: how many, and the crew fighting them (below the tractor warning)
            string boarded = carrier.Destroyed ? "" : Boarders.HudText(carrier.transform);
            if (boarded.Length > 0)
            {
                int cut = boarded.IndexOf('\n');
                Warn(Hud.Text(24, true, TextAnchor.MiddleCenter),
                     new GUIContent($"<color=#ff6050>{(cut > 0 ? boarded.Substring(0, cut) : boarded)}</color>" +
                                    (cut > 0 ? $"\n<size={Mathf.RoundToInt(16 * s)}>{boarded.Substring(cut + 1)}" +
                                               $"{(carrier.Clamped ? "; a boarding ship clamped to the hull" : "")}</size>" : "")),
                     Screen.height * 0.16f, pad2 * 2f);
            }

            if (toast != null && Clock < toastUntil)
            {
                var ts = Hud.Text(20, true, TextAnchor.MiddleCenter);
                var tc = new GUIContent(toast);
                Vector2 tsz = ts.CalcSize(tc);
                // (a long one, a comms message: wrapped, at most two thirds of the screen wide)
                float most = Screen.width * 0.66f;
                if (tsz.x > most)
                {
                    ts = new GUIStyle(ts) { wordWrap = true };
                    tsz = new Vector2(most, ts.CalcHeight(tc, most));
                }
                var r = new Rect((Screen.width - tsz.x) * 0.5f - pad2, Screen.height - tsz.y - pad2 * 6f, tsz.x + pad2 * 2f, tsz.y + pad2);
                if (r.Overlaps(ShipConsole.Rect)) r.y = ShipConsole.Rect.yMin - r.height - pad2;
                Hud.Panel(r);
                GUI.Label(r, tc, ts);
            }
        }
    }
}
