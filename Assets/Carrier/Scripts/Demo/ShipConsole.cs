using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The ship's console, in the bottom-left corner: the captain's portrait, an alert strip
    /// (under fire, missiles inbound, the shield down, the hull critical, enemies near), bars for
    /// the shield, hull, thrust, bow gun, warp drive and cargo, a row of weapon lights (guns
    /// bearing, CIWS, missiles, the wing), and a few lines of what is around (the target, the
    /// nearest place, landing and docking prompts, the mining laser and sensor).
    /// </summary>
    public class ShipConsole : MonoBehaviour
    {
        public CarrierDemoInput demo;

        /// <summary>Where the console is on screen (GUI coordinates), so other panels keep clear of it.</summary>
        public static Rect Rect { get; private set; }

        // the threat picture, updated a few times a second (the captain reads it too)
        public static int HostilesNear { get; private set; }
        public static float NearestHostile { get; private set; }
        public static int MissilesInbound { get; private set; }
        /// <summary>Hostiles near the ship that have been destroyed since the level started.</summary>
        public static int Kills { get; private set; }
        /// <summary>The alerts showing on the strip now (for the self-test).</summary>
        public static string Alerts { get; private set; } = "";

        Health hull;
        Shield shield;
        WarpDrive warp;
        CiwsDirector ciws;
        float nextScan;
        readonly System.Collections.Generic.HashSet<Health> alive = new System.Collections.Generic.HashSet<Health>();
        readonly System.Collections.Generic.List<Health> gone = new System.Collections.Generic.List<Health>();
        // (reused each time the GUI draws, rather than new ones every frame)
        readonly System.Collections.Generic.List<string> alerts = new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();

        static readonly Color Cyan = new Color(0.35f, 0.78f, 1f), Green = new Color(0.45f, 0.9f, 0.55f),
            Amber = new Color(1f, 0.72f, 0.3f), Red = new Color(1f, 0.32f, 0.25f), Dim = new Color(0.55f, 0.62f, 0.7f),
            Track = new Color(1f, 1f, 1f, 0.08f);

        void Start()
        {
            HostilesNear = 0; MissilesInbound = 0; Kills = 0; Alerts = "";
            var cc = demo != null ? demo.carrier : null;
            if (cc == null) return;
            hull = cc.GetComponent<Health>();
            shield = cc.GetComponentInChildren<Shield>(true);
            warp = cc.GetComponent<WarpDrive>();
            ciws = cc.GetComponent<CiwsDirector>();
        }

        void OnDestroy() => Rect = UnityEngine.Rect.zero;

        void Update()
        {
            var cc = demo != null ? demo.carrier : null;
            if (cc == null || Time.time < nextScan) return;
            nextScan = Time.time + 0.25f;
            // in the warp tunnel nothing can touch the ship (it only passes things by)
            if (warp != null && warp.Current == WarpDrive.State.Warping) { HostilesNear = 0; MissilesInbound = 0; return; }
            Vector3 me = cc.transform.position;
            int n = 0;
            float nearest = float.MaxValue;
            gone.Clear();
            foreach (var h in alive) if (h == null || h.Dead) gone.Add(h);
            foreach (var h in gone) { alive.Remove(h); if (h != null) Kills++; }
            foreach (var info in TargetInfo.All)
            {
                if (info == null || !info.Hostile) continue;
                if (!TargetDisplay.InRange(info.transform, me)) continue;
                var h = info.GetComponent<Health>();
                if (h != null && h.Dead) continue;
                if (CombatPilot.IsHidden(info.transform)) continue;
                n++;
                nearest = Mathf.Min(nearest, Vector3.Distance(info.transform.position, me));
                if (h != null) alive.Add(h);
            }
            HostilesNear = n;
            NearestHostile = n > 0 ? nearest : 0f;
            int m = 0;
            foreach (var missile in Missile.Active)
                if (missile != null && missile.target != null && missile.target.root == cc.transform.root) m++;
            MissilesInbound = m;
        }

        bool UnderFire
        {
            get
            {
                float last = Mathf.Max(hull != null ? hull.LastHitTime : -100f, shield != null ? shield.LastHitTime : -100f);
                return Time.time - last < 3f;
            }
        }

        void OnGUI()
        {
            var cc = demo != null ? demo.carrier : null;
            if (cc == null) return;
            float s = Hud.S;
            float pad = 10f * s, portrait = 196f * s, colW = 340f * s, row = 21f * s;
            bool flash = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f;

            // ---- what to show
            alerts.Clear();
            Color level = Green;
            if (cc.Destroyed) { alerts.Add("SHIP LOST"); level = Red; }
            else
            {
                if (UnderFire) { alerts.Add("UNDER FIRE"); level = Red; }
                if (MissilesInbound > 0) { alerts.Add(MissilesInbound == 1 ? "MISSILE INBOUND" : $"{MissilesInbound} MISSILES INBOUND"); level = Red; }
                if (shield != null && !shield.Up) { alerts.Add("SHIELD DOWN"); level = Red; }
                if (hull != null && hull.Current < hull.maxHealth * 0.25f) { alerts.Add("HULL CRITICAL"); level = Red; }
                if (HostilesNear > 0)
                {
                    alerts.Add($"{HostilesNear} HOSTILE{(HostilesNear == 1 ? "" : "S")}  {NearestHostile / 1000f:F1} KM");
                    if (level == Green) level = Amber;
                }
            }
            Alerts = string.Join(" · ", alerts);

            var gun = demo.MainGun;
            var cargo = demo.Cargo;
            int bars = 3 + (gun != null ? 1 : 0) + (warp != null ? 1 : 0) + (cargo != null ? 1 : 0);
            var info = InfoLines(cc);
            var small = Hud.Text(13);
            float infoH = info.Count * small.lineHeight * 1.05f;
            float colH = 22f * s + 6f * s + 26f * s + 6f * s + bars * row + 6f * s + 20f * s + 6f * s + infoH;
            float h = Mathf.Max(portrait + 26f * s, colH) + pad * 2f;
            float w = pad * 3f + portrait + colW;
            var panel = new Rect(pad, Screen.height - h - pad, w, h);
            Rect = panel;
            Hud.Panel(panel);
            Color edge = level == Red ? (flash ? Red : new Color(0.6f, 0.15f, 0.12f)) : level == Amber ? Amber : new Color(0.35f, 0.6f, 0.9f);
            Hud.Fill(new Rect(panel.x, panel.y, panel.width, Mathf.Max(1f, 2f * s)), edge);

            // ---- the captain
            var pr = new Rect(panel.x + pad, panel.y + pad, portrait, portrait);
            var cap = CaptainPortrait.Instance;
            if (cap != null) cap.Draw(pr, edge);
            else Hud.Fill(pr, new Color(0f, 0f, 0f, 0.5f));
            var who = Captain.Current;
            GUI.Label(new Rect(pr.x, pr.yMax + 4f * s, portrait, 18f * s), $"<b>CAPT. {who.name.ToUpperInvariant()}</b>", Hud.Text(12, false, TextAnchor.UpperCenter));

            // ---- the column of bars
            float x = pr.xMax + pad, y = panel.y + pad;
            string reason = cap != null ? cap.Reason : "";
            GUI.Label(new Rect(x, y, colW, 22f * s),
                $"<b>{cc.shipName.ToUpperInvariant()}</b>   <color=#{ColorUtility.ToHtmlStringRGB(Dim)}>{reason}</color>", Hud.Text(16));
            y += 22f * s + 6f * s;

            // the alert strip
            var strip = new Rect(x, y, colW, 26f * s);
            Color sc = level == Red ? new Color(0.75f, 0.12f, 0.08f, flash ? 0.85f : 0.55f)
                     : level == Amber ? new Color(0.65f, 0.42f, 0.08f, 0.6f) : new Color(0.1f, 0.35f, 0.18f, 0.5f);
            Hud.Fill(strip, sc);
            // several alerts at once: smaller, then shorter, so they all fit on the strip
            var room = new Rect(strip.x + 8f * s, strip.y, strip.width - 16f * s, strip.height);
            string st = alerts.Count > 0 ? $"<b>{string.Join("  ·  ", alerts)}</b>" : "<b>ALL CLEAR</b>";
            var font = Hud.Text(13, false, TextAnchor.MiddleLeft);
            if (font.CalcSize(new GUIContent(st)).x > room.width) font = Hud.Text(11, false, TextAnchor.MiddleLeft);
            if (font.CalcSize(new GUIContent(st)).x > room.width)
                st = $"<b>{string.Join(" · ", alerts).Replace("MISSILES INBOUND", "MSL").Replace("MISSILE INBOUND", "MSL").Replace("HOSTILES", "HOST.").Replace("HOSTILE", "HOST.")}</b>";
            GUI.Label(room, st, font);
            y += 26f * s + 6f * s;

            // shield, hull, thrust
            if (shield != null)
            {
                if (shield.Up) Bar(x, ref y, colW, row, "SHIELD", shield.Fraction, Cyan, $"{shield.Fraction:P0}");
                else Bar(x, ref y, colW, row, "SHIELD", shield.Fraction, flash ? Red : Red * 0.6f, "DOWN");
            }
            else Bar(x, ref y, colW, row, "SHIELD", 0f, Dim, "none");
            float hf = hull != null ? hull.Current / Mathf.Max(1f, hull.maxHealth) : 1f;
            Bar(x, ref y, colW, row, "HULL", hf, hf > 0.6f ? Green : hf > 0.3f ? Amber : Red, $"{hf:P0}");
            Bar(x, ref y, colW, row, "THRUST", cc.throttle, Amber, $"{cc.Speed:F0} m/s");

            // the bow gun
            if (gun != null)
            {
                switch (gun.Current)
                {
                    case MainBeam.State.Charging:
                        bool onTarget = cc.target != null && ShipAutopilot.BowError(cc, cc.target.position) <= gun.aimAssist;
                        Bar(x, ref y, colW, row, "MAIN GUN", gun.ChargeProgress, Cyan, $"{gun.ChargeTimeLeft:F1} s{(cc.target != null ? onTarget ? "  ON TARGET" : "  OFF" : "")}");
                        break;
                    case MainBeam.State.Firing:
                        Bar(x, ref y, colW, row, "MAIN GUN", 1f, flash ? Color.white : Cyan, "FIRING");
                        break;
                    default:
                        Bar(x, ref y, colW, row, "MAIN GUN", 1f, Green * 0.7f, "READY");
                        break;
                }
            }
            // the warp drive
            if (warp != null)
            {
                switch (warp.Current)
                {
                    case WarpDrive.State.Charging:
                        Bar(x, ref y, colW, row, "WARP", Mathf.Clamp01(warp.StateTime / Mathf.Max(0.1f, warp.chargeTime)), Cyan, "CHARGING");
                        break;
                    case WarpDrive.State.Warping:
                        Bar(x, ref y, colW, row, "WARP", 1f - Mathf.Clamp01(warp.TimeLeft / Mathf.Max(0.1f, warp.TravelTime)), Cyan, $"IN WARP  {warp.TimeLeft:F1} s");
                        break;
                    case WarpDrive.State.Arriving:
                        Bar(x, ref y, colW, row, "WARP", 1f, Cyan, "ARRIVING");
                        break;
                    default:
                        if (!string.IsNullOrEmpty(warp.lockedBecause)) Bar(x, ref y, colW, row, "WARP", 0f, Dim, warp.lockedBecause.ToUpperInvariant());
                        else if (warp.InCombat(out string why)) Bar(x, ref y, colW, row, "WARP", 0f, Red, "LOCKED: " + why.ToUpperInvariant());
                        else Bar(x, ref y, colW, row, "WARP", 1f, Green * 0.7f, "READY");
                        break;
                }
            }
            // the hold
            if (cargo != null)
                Bar(x, ref y, colW, row, "CARGO", cargo.Fraction, Amber, $"{cargo.Used:F0}/{cargo.capacity:F0} t" + (cargo.Credits > 0.5f ? $"  {cargo.Credits:N0} cr" : ""));
            y += 6f * s;

            // weapon lights
            float lx = x;
            string bearing = cc.target != null ? cc.GunsBearing() : "";
            Light(ref lx, y, "GUNS", cc.target == null ? Dim : bearing.Length > 0 ? Green : Amber, cc.mainTurrets != null && cc.mainTurrets.Length > 0 && cc.mainTurrets[0] != null && !cc.mainTurrets[0].autoFire ? "HOLD" : null);
            if (ciws != null) Light(ref lx, y, "CIWS", ciws.EngagedTarget != null ? (flash ? Red : Amber) : Dim, null);
            if (cc.HasMissiles && cc.missiles != null) Light(ref lx, y, $"MSL {cc.missiles.ReadyCells}", cc.missiles.ReadyCells > 0 ? Green : Dim, null);
            if (cc.HasHangar) Light(ref lx, y, $"WING {cc.Wing.Count}", cc.Wing.Count > 0 ? Cyan : Dim, null);
            if (demo.Laser != null) Light(ref lx, y, "LASER", demo.Laser.Cutting ? Amber : demo.Laser.On ? Cyan : Dim, null);
            y += 20f * s + 6f * s;

            // what is around
            foreach (var line in info)
            {
                GUI.Label(new Rect(x, y, colW + pad, small.lineHeight * 1.05f), line, small);
                y += small.lineHeight * 1.05f;
            }
        }

        /// <summary>A labelled bar with a value at its right-hand end.</summary>
        static void Bar(float x, ref float y, float w, float row, string label, float k, Color c, string value)
        {
            float s = Hud.S;
            float lw = 78f * s, vw = 128f * s;
            GUI.Label(new Rect(x, y, lw, row), $"<b>{label}</b>", Hud.Text(12, false, TextAnchor.MiddleLeft));
            var track = new Rect(x + lw, y + row * 0.22f, w - lw - vw - 6f * s, row * 0.56f);
            Hud.Fill(track, Track);
            if (k > 0f) Hud.Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(k), track.height), c);
            // ticks every 10 %
            for (int i = 1; i < 10; i++)
                Hud.Fill(new Rect(track.x + track.width * i / 10f, track.y, Mathf.Max(1f, s), track.height), new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(track.xMax + 6f * s, y, vw, row), value, Hud.Text(12, false, TextAnchor.MiddleLeft));
            y += row;
        }

        /// <summary>A small lit (or unlit) tag.</summary>
        static void Light(ref float x, float y, string text, Color c, string note)
        {
            float s = Hud.S;
            var st = Hud.Text(11, true, TextAnchor.MiddleCenter);
            string label = note != null ? $"{text} {note}" : text;
            float w = st.CalcSize(new GUIContent(label)).x + 14f * s;
            var r = new Rect(x, y, w, 18f * s);
            Hud.Fill(r, new Color(c.r, c.g, c.b, 0.22f));
            Hud.Fill(new Rect(r.x, r.y, Mathf.Max(2f, 3f * s), r.height), c);
            GUI.Label(r, label, st);
            x += w + 6f * s;
        }

        /// <summary>The target, the nearest place and the prompts that go with it, and the mining gear.</summary>
        System.Collections.Generic.List<string> InfoLines(CarrierController cc)
        {
            lines.Clear();
            string dim = "#" + ColorUtility.ToHtmlStringRGB(Dim);
            if (cc.target != null)
            {
                string name = cc.target.GetComponent<TargetInfo>() is TargetInfo ti && ti != null ? ti.Name : cc.target.name;
                lines.Add($"Target  {name}  <color={dim}>{Vector3.Distance(cc.target.position, cc.transform.position) / 1000f:F1} km</color>");
            }
            if (cc.HasHangar)
                lines.Add($"Hangar  fighters {cc.fighterStock}  drones {cc.droneStock}  dropships {cc.dropshipStock}");
            string dock = Docking.Instance != null ? Docking.Instance.HudText() : null;
            if (dock != null) lines.Add(dock);
            string land = PlanetLanding.Instance != null ? PlanetLanding.Instance.HudText() : null;
            if (land != null) lines.Add(land);
            if (warp != null && warp.Current == WarpDrive.State.Ready && !PlanetLanding.Busy)
            {
                var here = WarpPoint.Nearest(cc.transform.position);
                if (here != null) lines.Add($"Near  {here.displayName}  <color={dim}>{Vector3.Distance(here.Position, cc.transform.position) / 1000f:F0} km</color>");
            }
            var laser = demo.Laser;
            if (laser != null && laser.On) lines.Add($"Mining laser  <color=#ffc070>{laser.Status}</color>");
            var sensor = demo.Sensor;
            if (sensor != null)
            {
                bool pad = PadSupport.UsingPad && Gamepad.current != null;
                string what = PlanetLanding.OnSurface ? "veins" : "deposits";
                if (sensor.Sweeping) lines.Add("Sensor  <color=#bfe6ff>scanning</color>");
                else if (sensor.Traces > 0) lines.Add($"Sensor  traces of {sensor.Traces} {what} within {sensor.Range / 1000f:F0} km  <color={dim}>{(pad ? "Back+Y" : "V")} scan</color>");
            }
            if (SoundFX.Muted) lines.Add($"<color={dim}>sound off</color>");
            return lines;
        }
    }
}
