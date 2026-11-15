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
    public class ShipHud : MonoBehaviour
    {
        public CarrierDemoInput demo;

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
            || ModeMenu.Area.Contains(gui) || FleetDirector.PanelsRect.Contains(gui);

        // ------------------------------------------------------------ messages

        string toast;
        float toastUntil;
        // a reel runs on game time (locked to its frame rate), so its messages last as long on film as in play
        static float Clock => Hud.ReelMode ? Time.time : Time.unscaledTime;

        void Show(string message)
        {
            toast = message;
            toastUntil = Clock + 2.5f;
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

        const string Rule = "<color=#55606e>----------</color>";

        /// <summary>The controls list for the mode the player is in, leaving out systems this ship
        /// doesn't have, then the keys that are the same in every mode.</summary>
        string KeyHelp
        {
            get
            {
                var carrier = Carrier;
                HelpFor(carrier);
                if (keyHelp != null) return keyHelp;
                var lines = new System.Collections.Generic.List<string> { ModeBar(false) };
                switch (Modes.Current)
                {
                    case ModeOfPlay.Combat:
                        lines.Add("F fire guns   G auto-fire on/off" + (carrier.HasMissiles ? "   R missile salvo" : ""));
                        if (demo.MainGun != null) lines.Add("B main gun: charge (fires after 10 s)");
                        lines.Add("T next target (Shift+T back)   click a ship");
                        lines.Add("X stop targeting   C line up   U fly to the target");
                        if (KineticStrike.Instance != null) lines.Add("Y kinetic strike from orbit");
                        if (demo.Cargo != null) lines.Add("Z salvage a wreck (flies to it)   Shift+Z next wreck");
                        if (FleetDirector.Instance != null && FleetDirector.Instance.Ships.Count > 0) lines.Add("V the fleet attacks my target   Shift+V forms up");
                        lines.Add("Tab the targets and wrecks near");
                        break;
                    case ModeOfPlay.Mining:
                        if (demo.Laser == null) { lines.Add("<i>No mining gear on this ship</i>"); break; }
                        lines.Add("Z mining laser on/off   V mineral scan");
                        lines.Add("T next deposit (Shift+T back)   C fly to it");
                        lines.Add("I cargo hold   J jettison cheapest ore");
                        lines.Add("Tab the deposits found, and the ore aboard");
                        break;
                    case ModeOfPlay.Navigation:
                        if (demo.Warp != null) lines.Add("M system map: warp to a planet, moon, field...");
                        lines.Add("Tab the points of interest here: Enter flies there");
                        lines.Add("   (K there to land or dock, C to mark the course)");
                        lines.Add("X stop the auto-travel (or just steer)");
                        if (PlanetFlight.Instance != null) lines.Add("O cruise on/off (the higher, the faster)");
                        break;
                    case ModeOfPlay.Services:
                        lines.Add("Tab or U the station's or base's services");
                        lines.Add("   (docked, or landed on its pad): storage, refining,");
                        lines.Add("   building, fitting, trade, crew, the shipyard");
                        lines.Add("   (what the place offers)");
                        break;
                    case ModeOfPlay.Fleet:
                        if (FleetDirector.Instance == null || FleetDirector.Instance.Ships.Count == 0) { lines.Add("<i>No fleet yet: build a ship at a shipyard,</i>"); lines.Add("<i>and have her join you there (services)</i>"); break; }
                        lines.Add("F follow me   G guard me   R form up");
                        lines.Add("V attack my target   Z hold position");
                        lines.Add("B view the next ship (and back)");
                        lines.Add("Tab the fleet: choose who gets the orders");
                        break;
                }
                lines.Add(Rule);
                lines.Add("W/S throttle   A/D turn   Q/E roll");
                lines.Add(OnWeb ? "Up/Down arrows nose up/down   L level off" : "↑/↓ nose up/down   L level off");
                lines.Add("Space/Ctrl elevator thrusters up/down");
                lines.Add("Right-drag orbit   mouse wheel zoom");
                if (demo.Warp != null && Modes.Current != ModeOfPlay.Navigation) lines.Add("M system map");
                if (PlanetLanding.Instance != null) lines.Add(Docking.Instance != null ? "K land / take off, dock / undock" : "K land / take off (in orbit)");
                lines.Add("H hide help   +/- text size   N sound");
                lines.Add(OnWeb ? "F11 full screen   Esc or P menu" : "F11 window   Esc or P menu");
                return keyHelp = string.Join("\n", lines);
            }
        }

        string PadHelp
        {
            get
            {
                var carrier = Carrier;
                HelpFor(carrier);
                if (padHelp != null) return padHelp;
                var lines = new System.Collections.Generic.List<string> { ModeBar(true), "Back+LB/RB mode   Back+Y this mode's menu" };
                switch (Modes.Current)
                {
                    case ModeOfPlay.Combat:
                        lines.Add("RB fire guns" + (carrier.HasMissiles ? "   LB missile salvo" : "") + "   L3 auto-fire");
                        if (demo.MainGun != null) lines.Add("X main gun: charge");
                        lines.Add("Y next target   B stop targeting");
                        lines.Add("A line up on the target" + (demo.Cargo != null ? "   Back+X salvage" : ""));
                        break;
                    case ModeOfPlay.Mining:
                        if (demo.Laser == null) { lines.Add("<i>No mining gear on this ship</i>"); break; }
                        lines.Add("A mining laser on/off   Y mineral scan");
                        lines.Add("X fly to the chosen deposit");
                        lines.Add("RB cargo hold   B jettison cheapest ore");
                        break;
                    case ModeOfPlay.Navigation:
                        if (demo.Warp != null) lines.Add("R3 system map: warp");
                        lines.Add("Back+Y the points of interest here");
                        lines.Add("A fly to the course   LB fly there and land or dock");
                        lines.Add("B stop the auto-travel   X cruise on/off");
                        break;
                    case ModeOfPlay.Services:
                        lines.Add("A the station's or base's services");
                        break;
                    case ModeOfPlay.Fleet:
                        if (FleetDirector.Instance == null || FleetDirector.Instance.Ships.Count == 0) { lines.Add("<i>No fleet yet: build a ship at a shipyard</i>"); break; }
                        lines.Add("A follow me   X guard me   Y form up");
                        lines.Add("RB attack my target   B hold   LB view a ship");
                        break;
                }
                lines.Add(Rule);
                lines.Add("Left stick  throttle / turn");
                lines.Add(W("D-pad ↑/↓ nose   ←/→ roll   Back+A level off", "D-pad up/down nose, left/right roll   Back+A level off"));
                lines.Add("LT/RT elevator thrusters down/up");
                lines.Add(W("Right stick  orbit camera   Back + D-pad ←/→ zoom", "Right stick  orbit camera   Back + D-pad left/right zoom"));
                if (demo.Warp != null && Modes.Current != ModeOfPlay.Navigation) lines.Add("R3 system map");
                if (PlanetLanding.Instance != null) lines.Add(Docking.Instance != null ? "Back+B land / take off, dock / undock" : "Back+B land / take off");
                lines.Add("Back hide help   Start menu");
                lines.Add(W("Back + D-pad ↑↓ text size", "Back + D-pad up/down text size"));
                return padHelp = string.Join("\n", lines);
            }
        }

        // ------------------------------------------------------------ drawing

        void OnGUI()
        {
            var carrier = Carrier;
            if (carrier == null) return;
            float s = Hud.S;
            bool pad = PadSupport.UsingPad && Gamepad.current != null;
            float pad2 = 12f * s;
            helpRect = Rect.zero;
            if (!Hud.ReelMode)
            {
                // the controls (the ship's state is on the console, bottom left)
                var style = Hud.Text(showHelp ? 18 : 16);
                string text = showHelp ? (pad ? PadHelp : KeyHelp) : ModeBar(pad) + (pad ? "\n<i>Back: show controls</i>" : "\n<i>H show controls</i>");
                var content = new GUIContent(text);
                Vector2 sz = style.CalcSize(content);
                helpRect = new Rect(pad2, pad2, sz.x + pad2 * 2f, sz.y + pad2 * 2f);
                Hud.Panel(helpRect);
                GUI.Label(new Rect(helpRect.x + pad2, helpRect.y + pad2, sz.x, sz.y), content, style);
            }

            if (carrier.Destroyed)
            {
                var big = Hud.Text(40, true, TextAnchor.MiddleCenter);
                var msg = new GUIContent($"<color=#ff6050>{carrier.shipName.ToUpper()} DESTROYED</color>\n" +
                                         $"<size={Mathf.RoundToInt(22 * s)}>respawning in {Mathf.CeilToInt(carrier.RespawnIn)} s</size>");
                Vector2 bsz = big.CalcSize(msg);
                var br = new Rect((Screen.width - bsz.x) * 0.5f - pad2 * 2f, Screen.height * 0.28f, bsz.x + pad2 * 4f, bsz.y + pad2 * 2f);
                Hud.Panel(br);
                GUI.Label(br, msg, big);
            }

            if (toast != null && Clock < toastUntil)
            {
                var ts = Hud.Text(20, true, TextAnchor.MiddleCenter);
                var tc = new GUIContent(toast);
                Vector2 tsz = ts.CalcSize(tc);
                var r = new Rect((Screen.width - tsz.x) * 0.5f - pad2, Screen.height - tsz.y - pad2 * 6f, tsz.x + pad2 * 2f, tsz.y + pad2);
                if (r.Overlaps(ShipConsole.Rect)) r.y = ShipConsole.Rect.yMin - r.height - pad2;
                Hud.Panel(r);
                GUI.Label(r, tc, ts);
            }
        }
    }
}
