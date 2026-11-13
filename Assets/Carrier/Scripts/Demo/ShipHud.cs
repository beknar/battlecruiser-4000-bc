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
            return hud;
        }

        void OnEnable() => Notices.Posted += Show;
        void OnDisable() => Notices.Posted -= Show;

        public void ToggleHelp() => showHelp = !showHelp;

        /// <summary>A panel covers this point (GUI coordinates): a click there isn't on a ship.</summary>
        public bool Covers(Vector2 gui) =>
            helpRect.Contains(gui) || ShipConsole.Rect.Contains(gui) || (display != null && display.PanelRect.Contains(gui)) || MiningDisplay.ManifestRect.Contains(gui);

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
        // (the ship the lists were made for: another ship, taken command of, has other systems)
        object helpFor;

        void HelpFor(CarrierController carrier)
        {
            if (ReferenceEquals(helpFor, carrier)) return;
            helpFor = carrier;
            keyHelp = padHelp = null;
        }

        static bool OnWeb => LaunchArgs.OnWeb;
        /// <summary>The text for here, or for a browser: it has no system fonts to fall back on,
        /// and the built-in font has no arrows.</summary>
        static string W(string native, string web) => OnWeb ? web : native;

        /// <summary>The controls list, leaving out systems this ship doesn't have.</summary>
        string KeyHelp
        {
            get
            {
                var carrier = Carrier;
                HelpFor(carrier);
                return keyHelp ??= string.Join("\n", new[]
                {
                    carrier.HasHangar ? "1 fighters   2 drones   3 dropships" : null,
                    (carrier.HasMissiles ? (demo.Warp != null ? "4 missile salvo   " : "M missile salvo   ") : "") + "F fire guns   G auto-fire on/off",
                    demo.MainGun != null ? "B main gun: charge (fires after 10 s)" : null,
                    carrier.HasHangar ? "Q wing attack   E escort   R return to carrier" : null,
                    "W/S throttle   A/D turn",
                    OnWeb ? "Up/Down arrows nose up/down   L level off" : "↑/↓ nose up/down   L level off",
                    carrier.HasHangar ? null : "Q/E roll left/right",
                    "Space/Ctrl elevator thrusters up/down",
                    "T next target   click a ship to target",
                    "X stop targeting",
                    carrier.HasHangar ? "C camera: carrier / craft" : null,
                    "Right-drag orbit   mouse wheel zoom",
                    demo.Warp != null ? "M system map: mini-warp" : null,
                    PlanetLanding.Instance != null ? (Docking.Instance != null ? "K land / take off, dock / undock" : "K land / take off (in orbit)") : null,
                    demo.Laser != null ? "Z mining laser   V mineral scan" : null,
                    demo.Laser != null ? "I cargo hold   J jettison cheapest ore" : null,
                    IndustryPanel.Instance != null ? "U refinery / factory (docked or on its pad)" : null,
                    "H hide help   +/- text size   N sound",
                    OnWeb ? "F11 full screen   Esc or P menu" : "F11 window   Esc or P menu",
                }.Where(l => l != null));
            }
        }

        string PadHelp
        {
            get
            {
                var carrier = Carrier;
                HelpFor(carrier);
                return padHelp ??= string.Join("\n", new[]
                {
                    "<b>Gamepad</b>",
                    "Left stick  throttle / turn",
                    carrier.HasHangar ? "Right stick  orbit camera   LT/RT zoom" : W("Right stick  orbit camera   Back + D-pad ←/→ zoom", "Right stick  orbit camera   Back + D-pad left/right zoom"),
                    carrier.HasHangar ? W("D-pad  ↑ fighters  → drones  ↓ dropships  ← return", "D-pad  up fighters  right drones  down dropships  left return")
                                      : W("D-pad ↑/↓ nose up/down   ←/→ roll   A level off", "D-pad up/down nose, left/right roll   A level off"),
                    carrier.HasHangar ? null : "LT/RT elevator thrusters down/up",
                    carrier.HasHangar ? "A wing attack   X escort" : null,
                    "Y next target   B stop targeting",
                    "RB fire guns" + (carrier.HasMissiles ? (PlanetLanding.Instance != null ? "   LB missiles (with a target)" : "   LB missile salvo") : "") + (demo.MainGun != null && !carrier.HasHangar ? "   X main gun" : ""),
                    "L3 auto-fire on/off" + (carrier.HasHangar ? "   R3 camera: carrier / craft" : ""),
                    demo.Warp != null ? "R3 system map: mini-warp" : null,
                    PlanetLanding.Instance != null ? (Docking.Instance != null ? "LB land / take off, dock / undock" : "LB land / take off (in orbit)") + (carrier.HasMissiles ? " (no target)" : "") : null,
                    demo.Laser != null ? "Back+A mining laser   Back+Y mineral scan" : null,
                    demo.Laser != null ? "Back+X cargo hold   Back+B jettison" : null,
                    IndustryPanel.Instance != null ? "Back+RB refinery / factory screen" : null,
                    "Back hide help   Start menu",
                    W("Back + D-pad ↑↓ text size", "Back + D-pad up/down text size"),
                }.Where(l => l != null));
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
                string text = showHelp ? (pad ? PadHelp : KeyHelp) : (pad ? "<i>Back: show controls</i>" : "<i>H show controls</i>");
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
